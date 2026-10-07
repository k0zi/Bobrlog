using System.Globalization;
using System.Text.Json;
using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Analysis;

/// <summary>
/// Collects crash artefacts outside the journal: apport reports (/var/crash),
/// systemd-coredump (coredumpctl, if installed) and kernel pstore dumps (root only).
/// Unreadable sources are skipped silently; the service variant runs as root and sees all of them.
/// </summary>
public static class CrashReportReader
{
    public const string ApportDirectory = "/var/crash";
    public const string PstoreArchiveDirectory = "/var/lib/systemd/pstore";

    public static async Task<IReadOnlyList<CrashReport>> ReadAllAsync(CancellationToken ct = default)
    {
        var result = new List<CrashReport>();
        result.AddRange(ReadApport(ApportDirectory));
        result.AddRange(ReadPstore(PstoreArchiveDirectory));
        result.AddRange(await ReadCoredumpsAsync(ct).ConfigureAwait(false));
        result.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return result;
    }

    public static IEnumerable<CrashReport> ReadApport(string directory)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.crash");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            Dictionary<string, string> header;
            try
            {
                header = ReadApportHeader(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                header = new Dictionary<string, string>();
            }

            var exe = header.GetValueOrDefault("ExecutablePath") ?? GuessExecutableFromName(info.Name);
            var timestamp = header.TryGetValue("Date", out var date) && TryParseApportDate(date, out var parsed)
                ? parsed
                : new DateTimeOffset(info.LastWriteTime);
            var type = header.GetValueOrDefault("ProblemType") ?? "Crash";
            var details = new List<string>();
            foreach (var key in new[] { "ProblemType", "ExecutablePath", "Signal", "SignalName", "Package", "Title" })
                if (header.TryGetValue(key, out var v))
                    details.Add($"{key}: {v}");
            if (header.Count == 0)
                details.Add(CoreStrings.Crash_ReportUnreadable);

            yield return new CrashReport
            {
                Kind = CrashReportKind.Apport,
                Timestamp = timestamp,
                Title = header.GetValueOrDefault("Title") ?? $"{type}: {Path.GetFileName(exe)}",
                Executable = exe,
                Path = file,
                Details = string.Join('\n', details),
            };
        }
    }

    /// <summary>Reads top-level "Key: value" lines; stops before the (potentially huge) base64 sections.</summary>
    internal static Dictionary<string, string> ReadApportHeader(string file)
    {
        var header = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StreamReader(file);
        for (var lineNo = 0; lineNo < 5000; lineNo++)
        {
            var line = reader.ReadLine();
            if (line is null)
                break;
            if (line.Length == 0 || line[0] == ' ')
                continue;
            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = line[..colon];
            var value = line[(colon + 1)..].Trim();
            if (value == "base64")
                continue;
            header.TryAdd(key, value);
        }
        return header;
    }

    internal static bool TryParseApportDate(string value, out DateTimeOffset result)
    {
        // e.g. "Fri Oct  2 15:06:14 2026" (local time)
        var normalized = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (DateTime.TryParseExact(normalized, "ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dt))
        {
            result = new DateTimeOffset(dt);
            return true;
        }
        result = default;
        return false;
    }

    private static string GuessExecutableFromName(string fileName)
    {
        // "_usr_bin_gnome-shell.1000.crash" → "/usr/bin/gnome-shell"
        var name = fileName;
        var firstDot = name.IndexOf('.');
        if (firstDot > 0)
            name = name[..firstDot];
        return name.Replace('_', '/');
    }

    public static IEnumerable<CrashReport> ReadPstore(string directory)
    {
        string[] dirs;
        try
        {
            dirs = Directory.GetDirectories(directory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var dir in dirs)
        {
            string text;
            string[] files;
            try
            {
                files = Directory.GetFiles(dir);
                var dmesg = files.Where(f => Path.GetFileName(f).StartsWith("dmesg", StringComparison.Ordinal))
                    .OrderBy(f => f, StringComparer.Ordinal).ToArray();
                text = string.Join('\n', dmesg.Select(File.ReadAllText));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var tail = text.Length > 8000 ? text[^8000..] : text;
            var panicLine = tail.Split('\n').LastOrDefault(l =>
                l.Contains("Kernel panic", StringComparison.Ordinal) ||
                l.Contains("BUG:", StringComparison.Ordinal) ||
                l.Contains("Oops", StringComparison.Ordinal));

            yield return new CrashReport
            {
                Kind = CrashReportKind.Pstore,
                Timestamp = new DateTimeOffset(Directory.GetLastWriteTime(dir)),
                Title = panicLine is not null
                    ? string.Format(CultureInfo.CurrentCulture, CoreStrings.Crash_PstorePanic, panicLine.Trim())
                    : CoreStrings.Crash_PstoreLog,
                Path = dir,
                Details = tail,
            };
        }
    }

    public static async Task<IReadOnlyList<CrashReport>> ReadCoredumpsAsync(CancellationToken ct = default)
    {
        var coredumpctl = ProcessLines.FindExecutable("coredumpctl");
        if (coredumpctl is null)
            return [];

        try
        {
            var (code, stdout, _) = await ProcessLines.RunToEndAsync(coredumpctl,
                ["list", "--json=short", "--no-pager", "--since", "-30d"], ct).ConfigureAwait(false);
            if (code != 0 || string.IsNullOrWhiteSpace(stdout))
                return [];
            return ParseCoredumpList(stdout);
        }
        catch (Exception e) when (e is InvalidOperationException or JsonException or System.ComponentModel.Win32Exception)
        {
            return [];
        }
    }

    internal static IReadOnlyList<CrashReport> ParseCoredumpList(string json)
    {
        var list = new List<CrashReport>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var exe = item.TryGetProperty("exe", out var e) ? e.GetString() : null;
            var sig = item.TryGetProperty("sig", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0;
            var time = item.TryGetProperty("time", out var t) && t.ValueKind == JsonValueKind.Number
                ? JournalJsonParser.FromMicros(t.GetInt64())
                : DateTimeOffset.Now;
            var pid = item.TryGetProperty("pid", out var p) ? p.ToString() : "?";
            list.Add(new CrashReport
            {
                Kind = CrashReportKind.Coredump,
                Timestamp = time,
                Title = $"Coredump: {Path.GetFileName(exe ?? "?")} (signal {sig})",
                Executable = exe,
                Details = $"PID: {pid}\nSignal: {sig}\nExecutable: {exe}",
            });
        }
        return list;
    }
}
