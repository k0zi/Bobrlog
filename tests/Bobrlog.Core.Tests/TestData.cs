using Bobrlog.Core.Models;
using Bobrlog.Core.Rules;
using Bobrlog.Core.Sources;

namespace Bobrlog.Core.Tests;

internal static class TestData
{
    public static readonly RuleEngine Rules = RuleEngine.LoadDefault();

    private static long _seq;

    public static LogEntry Entry(string message, string identifier = "kernel", int priority = 6,
        string transport = "kernel", DateTimeOffset? at = null, string? unit = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["MESSAGE"] = message,
            ["SYSLOG_IDENTIFIER"] = identifier,
            ["PRIORITY"] = priority.ToString(),
            ["_TRANSPORT"] = transport,
        };
        var e = new LogEntry
        {
            Timestamp = at ?? DateTimeOffset.Now,
            Message = message,
            Priority = priority,
            Identifier = identifier,
            Transport = transport,
            Unit = unit,
            Cursor = "c" + Interlocked.Increment(ref _seq),
            Fields = fields,
        };
        e.Severity = SeverityMapper.FromPriority(priority);
        Rules.Apply(e);
        return e;
    }
}
