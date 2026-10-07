namespace Bobrlog.Core.Models;

public enum CrashReportKind
{
    Apport,
    Coredump,
    Pstore,
}

public sealed class CrashReport
{
    public required CrashReportKind Kind { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string Title { get; init; }
    public string? Executable { get; init; }
    public string? Path { get; init; }
    public string? Details { get; init; }
}
