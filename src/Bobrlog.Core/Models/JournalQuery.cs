namespace Bobrlog.Core.Models;

public sealed record JournalQuery
{
    public DateTimeOffset? Since { get; init; }
    public DateTimeOffset? Until { get; init; }
    /// <summary>Boot id, or null for all boots.</summary>
    public string? BootId { get; init; }
    public Severity? MinSeverity { get; init; }
    public bool KernelOnly { get; init; }
    public string? Unit { get; init; }
    public string? Identifier { get; init; }
    public string? Grep { get; init; }
    /// <summary>Maximum number of entries (newest first); null = unlimited.</summary>
    public int? Limit { get; init; } = 2000;
    /// <summary>Continue after this cursor in the query direction (older entries when <see cref="NewestFirst"/>, newer otherwise).</summary>
    public string? AfterCursor { get; init; }
    public bool NewestFirst { get; init; } = true;

    /// <summary>
    /// True when a newest-first, cursor-continued read has gone past <see cref="Since"/>
    /// (journalctl cannot combine --since with a cursor, so the reader has to stop itself).
    /// </summary>
    public bool IsPastRange(LogEntry entry) =>
        NewestFirst && AfterCursor is not null && Since is { } since && entry.Timestamp < since;
}
