using System.Globalization;
using System.Text.RegularExpressions;
using Bobrlog.Core.Models;
using Bobrlog.Core.Rules;
using Bobrlog.Core.Sources;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Analysis;

/// <summary>
/// Decides how a boot ended (clean power-off/reboot, unexpected stop, died in suspend)
/// and collects the most likely causes from the last minutes of the boot.
/// </summary>
public sealed partial class BootAnalyzer(IJournalSource source, RuleEngine rules)
{
    public const int TailSize = 300;
    public static readonly TimeSpan ProblemWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan NextBootWindow = TimeSpan.FromMinutes(10);

    public static string NoTraceReason => CoreStrings.Boot_NoTraceReason;

    [GeneratedRegex(@"System is rebooting|Reached target .*Reboot|will reboot now|reboot: Restarting system|Rebooting\.", RegexOptions.IgnoreCase)]
    private static partial Regex RebootPattern();

    [GeneratedRegex(@"System is (powering down|halting)|Reached target .*(Power-Off|Halt)|will power off now|reboot: Power down|Powering off\.|systemd-shutdown|Syncing filesystems and block devices|Reached target .*Shutdown|Journal stopped", RegexOptions.IgnoreCase)]
    private static partial Regex ShutdownPattern();

    [GeneratedRegex(@"PM: suspend entry|PM: hibernation entry|The system will suspend now|Entering sleep state|Reached target .*Sleep|Starting systemd-(suspend|hibernate|suspend-then-hibernate)\.service", RegexOptions.IgnoreCase)]
    private static partial Regex SuspendEntryPattern();

    [GeneratedRegex(@"PM: suspend exit|PM: hibernation exit|System returned from sleep|Finished systemd-(suspend|hibernate|suspend-then-hibernate)\.service|Stopped target .*Sleep", RegexOptions.IgnoreCase)]
    private static partial Regex SuspendExitPattern();

    public async Task AnalyzeAsync(BootSession boot, BootSession? nextBoot, IReadOnlyList<CrashReport> crashes,
        CancellationToken ct = default)
    {
        if (boot.IsCurrent)
        {
            boot.ShutdownKind = ShutdownKind.Running;
            var running = await CollectAsync(new JournalQuery
            {
                BootId = boot.BootId, MinSeverity = Severity.Error, Limit = 200,
            }, ct).ConfigureAwait(false);
            AddReasons(boot, running);
            boot.Analyzed = true;
            return;
        }

        var tail = await CollectAsync(new JournalQuery { BootId = boot.BootId, Limit = TailSize }, ct).ConfigureAwait(false);
        var problems = await CollectAsync(new JournalQuery
        {
            BootId = boot.BootId,
            Since = boot.LastEntry - ProblemWindow,
            MinSeverity = Severity.Warning,
            Limit = 1000,
        }, ct).ConfigureAwait(false);

        IReadOnlyList<LogEntry> nextStart = [];
        if (nextBoot is not null)
        {
            nextStart = await CollectAsync(new JournalQuery
            {
                BootId = nextBoot.BootId,
                Until = nextBoot.FirstEntry + NextBootWindow,
                Identifier = "systemd-pstore",
                Limit = 50,
            }, ct).ConfigureAwait(false);
        }

        Classify(boot, nextBoot, tail, problems, nextStart, crashes);
    }

    private async Task<IReadOnlyList<LogEntry>> CollectAsync(JournalQuery query, CancellationToken ct)
    {
        var list = new List<LogEntry>();
        await foreach (var e in source.QueryAsync(query, ct).ConfigureAwait(false))
            list.Add(e);
        return list;
    }

    /// <summary>Pure classification step (unit-testable). <paramref name="tail"/> may be in any order.</summary>
    public void Classify(BootSession boot, BootSession? nextBoot, IReadOnlyList<LogEntry> tail,
        IReadOnlyList<LogEntry> problems, IReadOnlyList<LogEntry> nextBootStart, IReadOnlyList<CrashReport> crashes)
    {
        var ordered = tail.OrderBy(e => e.Timestamp).ToList();

        var lastShutdown = ordered.FindLastIndex(e => ShutdownPattern().IsMatch(e.Message) || e.Identifier == "systemd-shutdown");
        var lastReboot = ordered.FindLastIndex(e => RebootPattern().IsMatch(e.Message));
        var lastSuspend = ordered.FindLastIndex(e => SuspendEntryPattern().IsMatch(e.Message));
        var lastResume = ordered.FindLastIndex(e => SuspendExitPattern().IsMatch(e.Message));

        if (lastReboot >= 0 && lastReboot >= lastSuspend)
            boot.ShutdownKind = ShutdownKind.Reboot;
        else if (lastShutdown >= 0 && lastShutdown >= lastSuspend)
            boot.ShutdownKind = ShutdownKind.PowerOff;
        else if (lastSuspend >= 0 && lastSuspend > lastResume)
            boot.ShutdownKind = ShutdownKind.SuspendNeverResumed;
        else
            boot.ShutdownKind = ShutdownKind.Unexpected;

        // The tail and the problem window overlap; count each entry once.
        var all = problems.Concat(ordered).Concat(nextBootStart)
            .DistinctBy(e => e.Cursor ?? e.Timestamp.ToString("O") + e.Message)
            .ToList();
        AddReasons(boot, all);

        // Program crashes (apport/coredump) shortly before the end, e.g. gnome-shell dying after a GPU reset.
        foreach (var crash in crashes.Where(c => c.Kind != CrashReportKind.Pstore &&
                                                 c.Timestamp >= boot.LastEntry - ProblemWindow &&
                                                 c.Timestamp <= boot.LastEntry + TimeSpan.FromMinutes(2))
                     .OrderByDescending(c => c.Timestamp))
        {
            AddReason(boot, string.Format(CultureInfo.CurrentCulture, CoreStrings.Boot_ProgramCrash,
                Path.GetFileName(crash.Executable ?? crash.Title), crash.Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture), crash.Kind));
        }

        if (nextBoot is not null)
        {
            foreach (var crash in crashes.Where(c => c.Kind == CrashReportKind.Pstore &&
                                                     c.Timestamp >= nextBoot.FirstEntry &&
                                                     c.Timestamp <= nextBoot.FirstEntry + NextBootWindow))
            {
                AddReason(boot, crash.Title);
            }
        }

        if (boot.ShutdownKind == ShutdownKind.SuspendNeverResumed)
            boot.Reasons.Insert(0, CoreStrings.Boot_SuspendNeverResumed);

        if (boot.ShutdownKind == ShutdownKind.Unexpected && boot.Reasons.Count == 0)
            boot.Reasons.Add(NoTraceReason);

        // Last errors before the end are the most useful evidence.
        boot.Evidence.AddRange(all
            .Where(e => e.Severity >= Severity.Error || rules.FindById(e.RuleId)?.Reason is not null)
            .OrderByDescending(e => e.Timestamp)
            .Take(50));

        boot.Analyzed = true;
    }

    private void AddReasons(BootSession boot, IEnumerable<LogEntry> entries)
    {
        // Strongest cause first: by severity, then most recent.
        var causes = entries
            .Select(e => (Entry: e, Rule: rules.FindById(e.RuleId)))
            .Where(x => x.Rule?.Reason is not null)
            .GroupBy(x => x.Rule!.Id)
            .Select(g => (Rule: g.First().Rule!, Count: g.Count(), Last: g.Max(x => x.Entry.Timestamp),
                Worst: g.Max(x => x.Entry.Severity)))
            .OrderByDescending(x => x.Worst)
            .ThenByDescending(x => x.Last);

        foreach (var c in causes)
        {
            var last = c.Last.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            AddReason(boot, c.Count > 1
                ? string.Format(CultureInfo.CurrentCulture, CoreStrings.Boot_ReasonRepeated, c.Rule.Reason, c.Count, last)
                : string.Format(CultureInfo.CurrentCulture, CoreStrings.Boot_ReasonOnce, c.Rule.Reason, last));
        }
    }

    private static void AddReason(BootSession boot, string reason)
    {
        if (!boot.Reasons.Contains(reason))
            boot.Reasons.Add(reason);
    }

    /// <summary>Analyzes all boots (newest first list) with limited parallelism.</summary>
    public async Task AnalyzeAllAsync(IReadOnlyList<BootSession> bootsNewestFirst, IReadOnlyList<CrashReport> crashes,
        int maxBoots = 30, CancellationToken ct = default)
    {
        var work = bootsNewestFirst.Take(maxBoots).Select((boot, i) => (boot, next: i > 0 ? bootsNewestFirst[i - 1] : null));
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    await AnalyzeAsync(item.boot, item.next, crashes, token).ConfigureAwait(false);
                }
                catch (JournalSourceException ex)
                {
                    item.boot.Reasons.Add(string.Format(CultureInfo.CurrentCulture, CoreStrings.Boot_AnalysisFailed, ex.Message));
                    item.boot.Analyzed = true;
                }
            }).ConfigureAwait(false);
    }
}
