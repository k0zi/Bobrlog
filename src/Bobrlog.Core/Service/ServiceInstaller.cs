using System.Diagnostics;
using System.Globalization;
using Bobrlog.Core.Platform;
using Bobrlog.Core.Sources;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Service;

public sealed record ServiceStatus(bool Installed, bool Active, bool Enabled, string ActiveState)
{
    public string DisplayText => !Installed ? CoreStrings.ServiceStatus_NotInstalled
        : Active ? CoreStrings.ServiceStatus_Running
        : string.Format(CultureInfo.CurrentCulture, CoreStrings.ServiceStatus_Stopped, ActiveState);
}

/// <summary>
/// Installs / removes the background service. The privileged part runs either in-process (when root)
/// or via <c>pkexec &lt;this app&gt; --install-service …</c>, which shows the GNOME password dialog.
/// </summary>
public static class ServiceInstaller
{
    public const string ServiceName = "bobrlog.service";
    public const string InstallDirectory = "/opt/bobrlog/service";
    public const string UnitPath = "/etc/systemd/system/" + ServiceName;
    public const string ExecutableName = "Bobrlog.Service";

    // Versions released under the old name (JournalReader) installed the service here; installing replaces it.
    private const string LegacyServiceName = "journalreader.service";
    private const string LegacyUnitPath = "/etc/systemd/system/" + LegacyServiceName;
    private const string LegacyInstallRoot = "/opt/journalreader";
    private const string LegacyConfigDirectory = "/etc/journalreader";

    public const string InstallArgument = "--install-service";
    public const string UninstallArgument = "--uninstall-service";
    public const string RestartArgument = "--restart-service";
    /// <summary>UI language for the messages of the privileged helper (pkexec clears the environment).</summary>
    public const string LanguageArgument = "--lang";

    public static string BundledServiceDirectory => Path.Combine(AppContext.BaseDirectory, "service");

    public static bool IsBundledServiceAvailable => File.Exists(Path.Combine(BundledServiceDirectory, ExecutableName));

    public static string UnitFileContent => $"""
        [Unit]
        Description=Bobrlog privileged log access service
        Documentation=file://{InstallDirectory}
        After=systemd-journald.service

        [Service]
        Type=notify
        ExecStart={InstallDirectory}/{ExecutableName}
        Restart=on-failure
        RestartSec=5
        RuntimeDirectory=bobrlog
        RuntimeDirectoryMode=0755
        # Higher priority than the GUI so queries stay responsive under load.
        Nice=-5
        IOSchedulingClass=best-effort
        IOSchedulingPriority=0
        Environment=DOTNET_EnableDiagnostics=0 DOTNET_gcServer=0 DOTNET_TieredPGO=0
        # Hardening: read-only access to the whole system, only reading capabilities.
        CapabilityBoundingSet=CAP_DAC_READ_SEARCH CAP_SYSLOG
        NoNewPrivileges=yes
        ProtectSystem=strict
        ProtectHome=yes
        PrivateTmp=yes
        PrivateDevices=yes
        PrivateNetwork=yes
        ProtectKernelTunables=yes
        ProtectKernelModules=yes
        ProtectControlGroups=yes
        ProtectClock=yes
        ProtectHostname=yes
        RestrictAddressFamilies=AF_UNIX
        RestrictNamespaces=yes
        RestrictRealtime=yes
        LockPersonality=yes

        [Install]
        WantedBy=multi-user.target

        """;

    public static async Task<ServiceStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var installed = File.Exists(UnitPath);
        var systemctl = ProcessLines.FindExecutable("systemctl") ?? "systemctl";
        var (_, active, _) = await ProcessLines.RunToEndAsync(systemctl, ["is-active", ServiceName], ct).ConfigureAwait(false);
        var (_, enabled, _) = await ProcessLines.RunToEndAsync(systemctl, ["is-enabled", ServiceName], ct).ConfigureAwait(false);
        var state = active.Trim();
        return new ServiceStatus(installed, state == "active", enabled.Trim() == "enabled", state);
    }

    // ───────────── unprivileged side ─────────────

    /// <summary>Runs install/uninstall/restart with root rights (pkexec unless already root).</summary>
    public static async Task<(bool Success, string Message)> RunElevatedAsync(string operationArgument, CancellationToken ct = default)
    {
        var uid = Native.CurrentUid;
        var opArgs = new List<string> { operationArgument };
        if (operationArgument == InstallArgument)
            opArgs.AddRange(["--uid", uid.ToString(CultureInfo.InvariantCulture), "--source", BundledServiceDirectory]);
        opArgs.AddRange([LanguageArgument, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName]);

        if (uid == 0)
        {
            try
            {
                var message = await ExecutePrivilegedAsync(opArgs.ToArray(), ct).ConfigureAwait(false);
                return (true, message);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        var pkexec = ProcessLines.FindExecutable("pkexec");
        if (pkexec is null)
            return (false, CoreStrings.Installer_PkexecMissing);

        var self = SelfCommand();
        var (code, stdout, stderr) = await ProcessLines.RunToEndAsync(pkexec, self.Concat(opArgs), ct).ConfigureAwait(false);
        return code switch
        {
            0 => (true, stdout.Trim()),
            126 => (false, CoreStrings.Installer_AuthCancelled),
            127 => (false, CoreStrings.Installer_AuthFailed),
            _ => (false, string.IsNullOrWhiteSpace(stderr) ? string.Format(CultureInfo.CurrentCulture, CoreStrings.Installer_ExitCode, code) : stderr.Trim()),
        };
    }

    /// <summary>The command that re-launches this application (handles "dotnet app.dll" during development).</summary>
    public static List<string> SelfCommand()
    {
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unknown");
        if (Path.GetFileNameWithoutExtension(processPath) == "dotnet")
        {
            var entry = global::System.Reflection.Assembly.GetEntryAssembly()?.Location
                        ?? throw new InvalidOperationException("Entry assembly unknown");
            return [processPath, entry];
        }
        return [processPath];
    }

    // ───────────── privileged side (runs as root) ─────────────

    /// <summary>Entry point for the privileged command line; returns a human readable result.</summary>
    public static async Task<string> ExecutePrivilegedAsync(string[] args, CancellationToken ct = default)
    {
        if (Native.CurrentUid != 0)
            throw new UnauthorizedAccessException(CoreStrings.Installer_RootRequired);

        switch (args[0])
        {
            case InstallArgument:
            {
                var uid = ArgValue(args, "--uid") ?? Environment.GetEnvironmentVariable("PKEXEC_UID") ?? Environment.GetEnvironmentVariable("SUDO_UID");
                if (uid is null || !uint.TryParse(uid, out var allowedUid))
                    throw new ArgumentException(CoreStrings.Installer_InvalidUid);
                var source = ArgValue(args, "--source") ?? BundledServiceDirectory;
                await InstallAsync(source, allowedUid, ct).ConfigureAwait(false);
                return CoreStrings.Installer_Installed;
            }
            case UninstallArgument:
                await UninstallAsync(ct).ConfigureAwait(false);
                return CoreStrings.Installer_Uninstalled;
            case RestartArgument:
                await SystemctlAsync(ct, "restart", ServiceName).ConfigureAwait(false);
                return CoreStrings.Installer_Restarted;
            default:
                throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, CoreStrings.Installer_UnknownOperation, args[0]));
        }
    }

    public static async Task InstallAsync(string sourceDirectory, uint allowedUid, CancellationToken ct = default)
    {
        var sourceExe = Path.Combine(sourceDirectory, ExecutableName);
        if (!File.Exists(sourceExe))
            throw new FileNotFoundException(string.Format(CultureInfo.CurrentCulture, CoreStrings.Installer_ServiceExeMissing, sourceExe));

        if (File.Exists(UnitPath))
            await SystemctlAsync(ct, "stop", ServiceName).ConfigureAwait(false);
        var legacyUids = await RemoveLegacyServiceAsync(ct).ConfigureAwait(false);

        if (Directory.Exists(InstallDirectory))
            Directory.Delete(InstallDirectory, recursive: true);
        CopyDirectory(sourceDirectory, InstallDirectory);
        File.SetUnixFileMode(Path.Combine(InstallDirectory, ExecutableName),
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Directory.CreateDirectory(ServiceProtocol.ConfigDirectory);
        var uids = ReadAllowedUids(ServiceProtocol.AllowedUidsPath);
        uids.UnionWith(legacyUids);
        uids.Add(allowedUid);
        await File.WriteAllLinesAsync(ServiceProtocol.AllowedUidsPath,
            uids.Order().Select(u => u.ToString(CultureInfo.InvariantCulture)), ct).ConfigureAwait(false);

        await File.WriteAllTextAsync(UnitPath, UnitFileContent, ct).ConfigureAwait(false);
        await SystemctlAsync(ct, "daemon-reload").ConfigureAwait(false);
        await SystemctlAsync(ct, "enable", "--now", ServiceName).ConfigureAwait(false);
    }

    /// <summary>Removes a service installed under the old name; returns the UIDs it allowed.</summary>
    private static async Task<HashSet<uint>> RemoveLegacyServiceAsync(CancellationToken ct)
    {
        var uids = ReadAllowedUids(Path.Combine(LegacyConfigDirectory, "allowed-uids"));
        if (File.Exists(LegacyUnitPath))
        {
            await SystemctlAsync(ct, "disable", "--now", LegacyServiceName).ConfigureAwait(false);
            File.Delete(LegacyUnitPath);
            await SystemctlAsync(ct, "daemon-reload").ConfigureAwait(false);
        }
        if (Directory.Exists(LegacyInstallRoot))
            Directory.Delete(LegacyInstallRoot, recursive: true);
        if (Directory.Exists(LegacyConfigDirectory))
            Directory.Delete(LegacyConfigDirectory, recursive: true);
        return uids;
    }

    public static async Task UninstallAsync(CancellationToken ct = default)
    {
        if (File.Exists(UnitPath))
            await SystemctlAsync(ct, "disable", "--now", ServiceName).ConfigureAwait(false);
        File.Delete(UnitPath);
        var parent = Path.GetDirectoryName(InstallDirectory)!;
        if (Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
        if (Directory.Exists(ServiceProtocol.ConfigDirectory))
            Directory.Delete(ServiceProtocol.ConfigDirectory, recursive: true);
        await SystemctlAsync(ct, "daemon-reload").ConfigureAwait(false);
        await RemoveLegacyServiceAsync(ct).ConfigureAwait(false);
    }

    public static HashSet<uint> ReadAllowedUids(string path)
    {
        var set = new HashSet<uint>();
        if (!File.Exists(path))
            return set;
        foreach (var line in File.ReadLines(path))
            if (uint.TryParse(line.Trim(), out var uid))
                set.Add(uid);
        return set;
    }

    public static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    private static async Task SystemctlAsync(CancellationToken ct, params string[] args)
    {
        var systemctl = ProcessLines.FindExecutable("systemctl") ?? "systemctl";
        var (code, _, stderr) = await ProcessLines.RunToEndAsync(systemctl, args, ct).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException($"systemctl {string.Join(' ', args)}: {stderr.Trim()}");
    }
}
