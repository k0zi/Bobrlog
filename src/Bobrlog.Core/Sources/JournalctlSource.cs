using System.Runtime.CompilerServices;
using System.Text.Json;
using Bobrlog.Core.Analysis;
using Bobrlog.Core.Models;
using Bobrlog.Core.Rules;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Sources;

/// <summary>Reads the journal by spawning <c>journalctl -o json</c> with the current user's rights.</summary>
public sealed class JournalctlSource(RuleEngine rules) : IJournalSource
{
    private readonly string _journalctl = ProcessLines.FindExecutable("journalctl") ?? "journalctl";

    public string DisplayName => CoreStrings.Source_Local;

    public async IAsyncEnumerable<LogEntry> QueryAsync(JournalQuery query, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var lines = ProcessLines.RunAsync(_journalctl, JournalctlArguments.Build(query), ct);
        await foreach (var line in JournalctlArguments.ApplyLimit(lines, query).ConfigureAwait(false))
        {
            var entry = JournalJsonParser.ParseLine(line);
            if (entry is null)
                continue;
            if (query.IsPastRange(entry))
                yield break;
            rules.Apply(entry);
            yield return entry;
        }
    }

    public IAsyncEnumerable<LogEntry> FollowAsync(JournalQuery query, CancellationToken ct = default) =>
        JournalFollower.FollowAsync(this, query, ct);

    public async Task<IReadOnlyList<BootSession>> ListBootsAsync(CancellationToken ct = default)
    {
        var (code, stdout, stderr) = await ProcessLines
            .RunToEndAsync(_journalctl, ["--list-boots", "-o", "json", "--no-pager"], ct).ConfigureAwait(false);
        if (code != 0 && string.IsNullOrWhiteSpace(stdout))
            throw new JournalSourceException("journalctl --list-boots: " + stderr.Trim());
        return ParseBootList(stdout);
    }

    public static IReadOnlyList<BootSession> ParseBootList(string json)
    {
        var result = new List<BootSession>();
        if (string.IsNullOrWhiteSpace(json))
            return result;

        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            result.Add(new BootSession
            {
                BootId = item.GetProperty("boot_id").GetString()!,
                Index = item.GetProperty("index").GetInt32(),
                FirstEntry = JournalJsonParser.FromMicros(item.GetProperty("first_entry").GetInt64()),
                LastEntry = JournalJsonParser.FromMicros(item.GetProperty("last_entry").GetInt64()),
            });
        }
        // Newest first is what the UI wants.
        result.Sort((a, b) => b.Index.CompareTo(a.Index));
        return result;
    }

    public Task<IReadOnlyList<CrashReport>> GetCrashReportsAsync(CancellationToken ct = default) =>
        CrashReportReader.ReadAllAsync(ct);
}
