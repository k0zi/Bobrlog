using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Sources;

/// <summary>Runs a process and streams its stdout line by line.</summary>
public static class ProcessLines
{
    public static async IAsyncEnumerable<string> RunAsync(
        string fileName, IEnumerable<string> arguments, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        // Force stable, untranslated output.
        psi.Environment["LC_ALL"] = "C.UTF-8";
        psi.Environment["SYSTEMD_COLORS"] = "0";

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, CoreStrings.Process_CannotStart, fileName, ex.Message), ex);
        }

        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var reg = ct.Register(() => Kill(process));

        try
        {
            while (true)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                if (line is null)
                    break;
                yield return line;
            }
        }
        finally
        {
            // Also runs when the consumer stops early (break / limit) → never leave journalctl running.
            Kill(process);
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var err = (await stderr.ConfigureAwait(false)).Trim();
            // journalctl returns 1 with "-- No entries --" style output for empty results; only surface real errors.
            if (err.Length > 0 && !err.StartsWith("-- No entries", StringComparison.Ordinal))
                throw new JournalSourceException($"{Path.GetFileName(fileName)}: {err}");
        }
    }

    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunToEndAsync(
        string fileName, IEnumerable<string> arguments, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);
        psi.Environment["LC_ALL"] = "C.UTF-8";

        using var process = Process.Start(psi)!;
        using var reg = ct.Register(() => Kill(process));
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return (process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    public static string? FindExecutable(string name)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':')
                     .Concat(["/usr/bin", "/bin", "/usr/sbin", "/sbin"]))
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}

public sealed class JournalSourceException(string message) : Exception(message);
