using System.Globalization;
using Bobrlog.Core.Models;

namespace Bobrlog.Core.Sources;

public static class JournalctlArguments
{
    /// <remarks>
    /// Every value uses the "--option=value" form so that values starting with '-' can never be
    /// parsed as extra options (the service passes client-supplied values through here).
    /// </remarks>
    public static List<string> Build(JournalQuery query)
    {
        var args = new List<string> { "-o", "json", "--no-pager", "-q" };

        if (query.BootId is { } boot)
            args.Add("--boot=" + boot);
        // journalctl rejects --since together with a cursor; the reader enforces Since instead (see IsPastRange).
        if (query.Since is { } since && query.AfterCursor is null)
            args.Add("--since=" + FormatTimestamp(since));
        if (query.Until is { } until)
            args.Add("--until=" + FormatTimestamp(until));
        if (query.MinSeverity is { } sev)
            args.Add($"--priority=0..{SeverityMapper.ToMaxPriority(sev)}");
        if (query.KernelOnly)
            args.Add("_TRANSPORT=kernel");
        if (!string.IsNullOrWhiteSpace(query.Unit))
            args.Add("--unit=" + query.Unit);
        if (!string.IsNullOrWhiteSpace(query.Identifier))
            args.Add("--identifier=" + query.Identifier);
        if (!string.IsNullOrWhiteSpace(query.Grep))
            args.AddRange(["--case-sensitive=false", "--grep=" + query.Grep]);

        if (query.NewestFirst)
            args.Add("--reverse");
        if (query.AfterCursor is { } cursor)
            args.Add("--after-cursor=" + cursor);
        // --lines means "the last N" in journalctl; ascending limits are enforced by the reader instead.
        if (query.Limit is { } limit && query.NewestFirst)
            args.Add("--lines=" + Math.Max(limit, 1).ToString(CultureInfo.InvariantCulture));

        return args;
    }

    /// <summary>Stops an ascending stream after <see cref="JournalQuery.Limit"/> lines.</summary>
    public static async IAsyncEnumerable<string> ApplyLimit(IAsyncEnumerable<string> lines, JournalQuery query)
    {
        var remaining = query.NewestFirst ? int.MaxValue : query.Limit ?? int.MaxValue;
        if (remaining <= 0)
            yield break;
        await foreach (var line in lines.ConfigureAwait(false))
        {
            yield return line;
            if (--remaining == 0)
                yield break;
        }
    }

    /// <summary>"@seconds.micros" is accepted by journalctl and avoids locale/timezone ambiguity.</summary>
    public static string FormatTimestamp(DateTimeOffset value)
    {
        var micros = (value.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10;
        return "@" + (micros / 1_000_000).ToString(CultureInfo.InvariantCulture) + "." +
               (micros % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Shell-quoted command line, for "copy as journalctl command".</summary>
    public static string ToCommandLine(JournalQuery query)
    {
        var args = Build(query).Where(a => a != "-q" && a != "--no-pager").ToList();
        // Drop "-o json" for human use.
        var i = args.IndexOf("-o");
        if (i >= 0) args.RemoveRange(i, 2);
        return "journalctl " + string.Join(' ', args.Select(Quote));
    }

    private static string Quote(string arg) =>
        arg.All(c => char.IsLetterOrDigit(c) || "-_.@:/=".Contains(c)) ? arg : "'" + arg.Replace("'", "'\\''") + "'";
}
