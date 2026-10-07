using Bobrlog.Core.Analysis;
using Bobrlog.Core.Models;
using Bobrlog.Core.Service;

namespace Bobrlog.Core.Tests;

public class CrashAndProtocolTests
{
    [Fact]
    public void Reads_apport_header_and_skips_base64()
    {
        var dir = Directory.CreateTempSubdirectory("jr-apport");
        try
        {
            var file = Path.Combine(dir.FullName, "_usr_bin_foo.1000.crash");
            File.WriteAllText(file, """
                ProblemType: Crash
                Date: Fri Oct  2 15:06:14 2026
                Dependencies:
                 adduser 3.153
                ExecutablePath: /usr/bin/foo
                CoreDump: base64
                 H4sICAAAAAAC/0NvcmVEdW1wAA==
                Signal: 11
                """);

            var report = Assert.Single(CrashReportReader.ReadApport(dir.FullName));
            Assert.Equal(CrashReportKind.Apport, report.Kind);
            Assert.Equal("/usr/bin/foo", report.Executable);
            Assert.Equal(new DateTime(2026, 10, 2, 15, 6, 14), report.Timestamp.DateTime);
            Assert.Contains("Signal: 11", report.Details);
            Assert.DoesNotContain("H4sI", report.Details);
        }
        finally
        {
            dir.Delete(true);
        }
    }

    [Fact]
    public void Missing_crash_directory_is_empty() =>
        Assert.Empty(CrashReportReader.ReadApport("/nonexistent/jr"));

    [Fact]
    public void Request_roundtrip()
    {
        var request = new ServiceRequest(ServiceOperation.Query, new JournalQuery
        {
            BootId = "abc", MinSeverity = Severity.Error, Since = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), Limit = 5,
        });
        var json = ServiceProtocol.Serialize(request);
        Assert.Contains("\"op\":\"Query\"", json);
        Assert.DoesNotContain('\n', json);

        var back = ServiceProtocol.DeserializeRequest(json)!;
        Assert.Equal(request.Op, back.Op);
        Assert.Equal(request.Query, back.Query);
    }

    [Fact]
    public void Error_lines_are_recognized()
    {
        var line = ServiceProtocol.Error("nope \"quoted\"");
        Assert.Equal("nope \"quoted\"", ServiceProtocol.TryGetError(line));
        Assert.Null(ServiceProtocol.TryGetError("""{"MESSAGE":"x"}"""));
    }

    [Fact]
    public void Crash_list_roundtrip()
    {
        var list = new List<CrashReport>
        {
            new() { Kind = CrashReportKind.Pstore, Timestamp = DateTimeOffset.Now, Title = "t", Details = "d" },
        };
        var back = ServiceProtocol.DeserializeCrashes(ServiceProtocol.Serialize(list));
        Assert.Equal("t", Assert.Single(back).Title);
        Assert.Equal(CrashReportKind.Pstore, back[0].Kind);
    }

    [Fact]
    public void Unit_file_contains_priority_and_hardening()
    {
        var unit = ServiceInstaller.UnitFileContent;
        Assert.Contains("Nice=-5", unit);
        Assert.Contains("ProtectSystem=strict", unit);
        Assert.Contains("ExecStart=/opt/bobrlog/service/Bobrlog.Service", unit);
        Assert.DoesNotContain("\r", unit);
    }
}
