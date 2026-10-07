using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;

namespace Bobrlog.Core.Tests;

public class JournalJsonParserTests
{
    private const string KernelLine =
        """{"_HOSTNAME":"host","PRIORITY":"3","MESSAGE":"amdgpu 0000:c6:00.0: GPU reset begin!","SYSLOG_IDENTIFIER":"kernel","_BOOT_ID":"49c3923f4d3349fbbf3f19a395d93321","__REALTIME_TIMESTAMP":"1791364616219927","__CURSOR":"s=abc;i=1","_TRANSPORT":"kernel"}""";

    [Fact]
    public void Parses_basic_fields()
    {
        var e = JournalJsonParser.ParseLine(KernelLine)!;

        Assert.Equal("amdgpu 0000:c6:00.0: GPU reset begin!", e.Message);
        Assert.Equal(3, e.Priority);
        Assert.Equal(Severity.Error, e.Severity);
        Assert.Equal("49c3923f4d3349fbbf3f19a395d93321", e.BootId);
        Assert.Equal("kernel", e.Source);
        Assert.True(e.IsKernel);
        Assert.Equal("s=abc;i=1", e.Cursor);
        Assert.Equal(1791364616219927L, (e.Timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / 10);
    }

    [Fact]
    public void Decodes_byte_array_message()
    {
        // "Hi\x01!" as a byte array, as journalctl emits for non-printable content.
        var line = """{"MESSAGE":[72,105,1,33],"__REALTIME_TIMESTAMP":"1000000"}""";
        var e = JournalJsonParser.ParseLine(line)!;
        Assert.Equal("Hi !", e.Message);
    }

    [Fact]
    public void Joins_repeated_fields_and_tolerates_null()
    {
        var line = """{"MESSAGE":null,"TAG":["a","b"],"__REALTIME_TIMESTAMP":"1000000"}""";
        var e = JournalJsonParser.ParseLine(line)!;
        Assert.Equal(string.Empty, e.Message);
        Assert.Equal("a\nb", e.Field("TAG"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"MESSAGE":"no timestamp"}""")]
    public void Invalid_lines_return_null(string line) => Assert.Null(JournalJsonParser.ParseLine(line));

    [Fact]
    public void Prefers_UNIT_over_emitting_unit()
    {
        var line = """{"MESSAGE":"x","UNIT":"foo.service","_SYSTEMD_UNIT":"init.scope","__REALTIME_TIMESTAMP":"1000000"}""";
        Assert.Equal("foo.service", JournalJsonParser.ParseLine(line)!.Unit);
    }

    [Fact]
    public void Parses_boot_list_newest_first()
    {
        var json = """[{"index":-1,"boot_id":"a","first_entry":1000000,"last_entry":2000000},{"index":0,"boot_id":"b","first_entry":3000000,"last_entry":4000000}]""";
        var boots = JournalctlSource.ParseBootList(json);
        Assert.Equal(["b", "a"], boots.Select(b => b.BootId));
        Assert.True(boots[0].IsCurrent);
    }
}
