namespace Bobrlog.Core.Models;

public sealed class LogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required string Message { get; init; }
    public int Priority { get; init; } = 6;
    public string? BootId { get; init; }
    public string? Identifier { get; init; }
    public string? Unit { get; init; }
    public string? UserUnit { get; init; }
    public int? Pid { get; init; }
    public string? Command { get; init; }
    public string? Transport { get; init; }
    public string? Cursor { get; init; }
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>Set by the rule engine; defaults to the syslog priority mapping.</summary>
    public Severity Severity { get; set; }
    public EventCategory Category { get; set; }
    public string? RuleId { get; set; }
    public string? Explanation { get; set; }

    public bool IsKernel => Transport == "kernel";

    /// <summary>Best human-readable source name.</summary>
    public string Source => Identifier ?? Command ?? (IsKernel ? "kernel" : Unit ?? "?");

    public string? AnyUnit => Unit ?? UserUnit;

    public string FirstLine
    {
        get
        {
            var idx = Message.IndexOf('\n');
            return idx < 0 ? Message : Message[..idx];
        }
    }

    public string? Field(string name) => Fields.TryGetValue(name, out var v) ? v : null;
}
