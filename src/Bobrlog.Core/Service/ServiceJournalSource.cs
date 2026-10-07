using System.Runtime.CompilerServices;
using Bobrlog.Core.Models;
using Bobrlog.Core.Rules;
using Bobrlog.Core.Sources;
using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Service;

/// <summary>Reads the journal through the privileged background service.</summary>
public sealed class ServiceJournalSource(ServiceClient client, RuleEngine rules) : IJournalSource
{
    public string DisplayName => CoreStrings.Source_Service;

    public async IAsyncEnumerable<LogEntry> QueryAsync(JournalQuery query, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var entry in StreamAsync(new ServiceRequest(ServiceOperation.Query, query), ct).ConfigureAwait(false))
        {
            // Leaving the loop closes the connection, which stops journalctl in the service.
            if (query.IsPastRange(entry))
                yield break;
            yield return entry;
        }
    }

    public IAsyncEnumerable<LogEntry> FollowAsync(JournalQuery query, CancellationToken ct = default) =>
        JournalFollower.FollowAsync(this, query, ct);

    private async IAsyncEnumerable<LogEntry> StreamAsync(ServiceRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var line in client.RequestAsync(request, ct).ConfigureAwait(false))
        {
            var entry = JournalJsonParser.ParseLine(line);
            if (entry is null)
                continue;
            rules.Apply(entry);
            yield return entry;
        }
    }

    public async Task<IReadOnlyList<BootSession>> ListBootsAsync(CancellationToken ct = default)
    {
        await foreach (var line in client.RequestAsync(new ServiceRequest(ServiceOperation.Boots), ct).ConfigureAwait(false))
            return JournalctlSource.ParseBootList(line);
        return [];
    }

    public async Task<IReadOnlyList<CrashReport>> GetCrashReportsAsync(CancellationToken ct = default)
    {
        await foreach (var line in client.RequestAsync(new ServiceRequest(ServiceOperation.Crashes), ct).ConfigureAwait(false))
            return ServiceProtocol.DeserializeCrashes(line);
        return [];
    }
}
