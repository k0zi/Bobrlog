using Bobrlog.Core.Rules;
using Bobrlog.Core.Service;

namespace Bobrlog.Core.Sources;

public static class SourceSelector
{
    /// <summary>Uses the background service when it answers, otherwise the local journalctl.</summary>
    public static async Task<IJournalSource> SelectAsync(RuleEngine rules, bool preferService = true, CancellationToken ct = default)
    {
        if (preferService)
        {
            var client = new ServiceClient();
            if (await client.PingAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false) is not null)
                return new ServiceJournalSource(client, rules);
        }
        return new JournalctlSource(rules);
    }
}
