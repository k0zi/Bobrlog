using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;

namespace Bobrlog.Core.Tests;

public class JournalctlArgumentsTests
{
    [Theory]
    [InlineData(0, Severity.Critical)]
    [InlineData(2, Severity.Critical)]
    [InlineData(3, Severity.Error)]
    [InlineData(4, Severity.Warning)]
    [InlineData(5, Severity.Info)]
    [InlineData(6, Severity.Info)]
    [InlineData(7, Severity.Verbose)]
    public void Maps_priority_to_severity(int priority, Severity expected) =>
        Assert.Equal(expected, SeverityMapper.FromPriority(priority));

    [Fact]
    public void Builds_filters_in_option_equals_value_form()
    {
        var args = JournalctlArguments.Build(new JournalQuery
        {
            BootId = "--file=/etc/shadow",
            MinSeverity = Severity.Warning,
            Unit = "-x",
            Grep = "foo",
            KernelOnly = true,
            Limit = 10,
        });

        Assert.Contains("--boot=--file=/etc/shadow", args);
        Assert.Contains("--priority=0..4", args);
        Assert.Contains("--unit=-x", args);
        Assert.Contains("--grep=foo", args);
        Assert.Contains("_TRANSPORT=kernel", args);
        Assert.Contains("--reverse", args);
        Assert.Contains("--lines=10", args);
        Assert.DoesNotContain("--file=/etc/shadow", args);
    }

    [Fact]
    public void Ascending_query_does_not_use_lines_but_reader_limits()
    {
        var query = new JournalQuery { NewestFirst = false, Limit = 2 };
        var args = JournalctlArguments.Build(query);
        Assert.DoesNotContain(args, a => a.StartsWith("--lines", StringComparison.Ordinal));
        Assert.DoesNotContain("--reverse", args);
    }

    [Fact]
    public async Task ApplyLimit_stops_ascending_stream()
    {
        static async IAsyncEnumerable<string> Lines()
        {
            for (var i = 0; i < 10; i++)
            {
                await Task.Yield();
                yield return i.ToString();
            }
        }

        var result = new List<string>();
        await foreach (var l in JournalctlArguments.ApplyLimit(Lines(), new JournalQuery { NewestFirst = false, Limit = 3 }))
            result.Add(l);
        Assert.Equal(["0", "1", "2"], result);
    }

    [Fact]
    public void Formats_timestamp_as_epoch_micros()
    {
        var ts = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).AddTicks(1234560);
        Assert.Equal("@1700000000.123456", JournalctlArguments.FormatTimestamp(ts));
    }

    [Fact]
    public void Command_line_is_shell_quoted()
    {
        var cmd = JournalctlArguments.ToCommandLine(new JournalQuery { Grep = "it's", Limit = null });
        Assert.Equal("journalctl --case-sensitive=false '--grep=it'\\''s' --reverse", cmd);
    }
}

public class CursorRangeTests
{
    [Fact]
    public void Since_is_dropped_when_continuing_from_cursor_and_enforced_by_reader()
    {
        var since = DateTimeOffset.Now.AddDays(-1);
        var query = new JournalQuery { Since = since, AfterCursor = "c1" };
        var args = JournalctlArguments.Build(query);

        Assert.DoesNotContain(args, a => a.StartsWith("--since", StringComparison.Ordinal));
        Assert.Contains("--after-cursor=c1", args);
        Assert.True(query.IsPastRange(new LogEntry { Timestamp = since.AddSeconds(-1), Message = "" }));
        Assert.False(query.IsPastRange(new LogEntry { Timestamp = since.AddSeconds(1), Message = "" }));
        Assert.False((query with { AfterCursor = null }).IsPastRange(new LogEntry { Timestamp = since.AddDays(-5), Message = "" }));
    }
}
