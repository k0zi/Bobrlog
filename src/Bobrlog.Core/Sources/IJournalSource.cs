using Bobrlog.Core.Models;

namespace Bobrlog.Core.Sources;

public interface IJournalSource
{
    /// <summary>Human readable description of the source (shown in the status bar).</summary>
    string DisplayName { get; }

    IAsyncEnumerable<LogEntry> QueryAsync(JournalQuery query, CancellationToken ct = default);

    /// <summary>Streams new entries as they are written (like <c>journalctl -f</c>, but polling-based).</summary>
    IAsyncEnumerable<LogEntry> FollowAsync(JournalQuery query, CancellationToken ct = default);

    Task<IReadOnlyList<BootSession>> ListBootsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<CrashReport>> GetCrashReportsAsync(CancellationToken ct = default);
}
