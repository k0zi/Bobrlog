using Bobrlog.Core.Analysis;
using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;

namespace Bobrlog.Core.Tests;

[UseCulture("en-US")]
public class BootAnalyzerTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 6, 22, 0, 0, TimeSpan.FromHours(2));

    private static BootSession Boot(int index = -1) => new()
    {
        BootId = "boot" + index, Index = index, FirstEntry = End.AddHours(-5), LastEntry = End,
    };

    private static BootAnalyzer Analyzer() => new(new NullSource(), TestData.Rules);

    private static LogEntry At(int secondsBeforeEnd, string message, string identifier = "kernel", int priority = 6,
        string transport = "kernel") =>
        TestData.Entry(message, identifier, priority, transport, End.AddSeconds(-secondsBeforeEnd));

    [Fact]
    public void Clean_power_off()
    {
        var boot = Boot();
        Analyzer().Classify(boot, null,
        [
            At(30, "System is powering down.", "systemd-logind", 6, "journal"),
            At(5, "Reached target poweroff.target - System Power Off.", "systemd", 6, "journal"),
        ], [], [], []);

        Assert.Equal(ShutdownKind.PowerOff, boot.ShutdownKind);
        Assert.Empty(boot.Reasons);
    }

    [Fact]
    public void Clean_reboot()
    {
        var boot = Boot();
        Analyzer().Classify(boot, null, [At(5, "System is rebooting.", "systemd-logind", 6, "journal")], [], [], []);
        Assert.Equal(ShutdownKind.Reboot, boot.ShutdownKind);
    }

    [Fact]
    public void Unexpected_without_trace_gets_generic_explanation()
    {
        var boot = Boot();
        Analyzer().Classify(boot, null, [At(5, "usb 3-1: new device"), At(1, "wlan0: associated")], [], [], []);

        Assert.Equal(ShutdownKind.Unexpected, boot.ShutdownKind);
        Assert.Equal([BootAnalyzer.NoTraceReason], boot.Reasons);
    }

    [Fact]
    public void Unexpected_with_gpu_hang_and_lockup_lists_strongest_cause_first()
    {
        var boot = Boot();
        LogEntry[] problems =
        [
            At(120, "amdgpu 0000:c6:00.0: [drm] *ERROR* ring gfx_0.0.0 timeout, signaled seq=1"),
            At(60, "amdgpu 0000:c6:00.0: GPU reset begin!"),
            At(10, "watchdog: BUG: soft lockup - CPU#2 stuck for 23s!"),
        ];
        Analyzer().Classify(boot, null, problems, problems, [], []);

        Assert.Equal(ShutdownKind.Unexpected, boot.ShutdownKind);
        Assert.StartsWith("System freeze", boot.Reasons[0]);
        Assert.Contains(boot.Reasons, r => r.StartsWith("Graphics card", StringComparison.Ordinal) && r.Contains("2×"));
        Assert.NotEmpty(boot.Evidence);
        Assert.Equal(problems[2].Message, boot.Evidence[0].Message);
    }

    [Fact]
    public void Suspend_without_resume()
    {
        var boot = Boot();
        Analyzer().Classify(boot, null,
        [
            At(600, "PM: suspend entry (s2idle)"),
            At(500, "PM: suspend exit"),
            At(10, "PM: suspend entry (s2idle)"),
        ], [], [], []);

        Assert.Equal(ShutdownKind.SuspendNeverResumed, boot.ShutdownKind);
        Assert.StartsWith("The machine went to sleep", boot.Reasons[0]);
    }

    [Fact]
    [UseCulture("hu-HU")]
    public void Reasons_follow_the_ui_language()
    {
        var boot = Boot();
        LogEntry[] problems =
        [
            At(20, "watchdog: BUG: soft lockup - CPU#2 stuck for 23s!"),
            At(10, "watchdog: BUG: soft lockup - CPU#3 stuck for 23s!"),
        ];
        Analyzer().Classify(boot, null, problems, problems, [], []);

        Assert.StartsWith("Rendszerfagyás", boot.Reasons[0]);
        Assert.Contains("2×, utoljára", boot.Reasons[0]);
    }

    [Fact]
    public void Shutdown_after_resume_is_clean()
    {
        var boot = Boot();
        Analyzer().Classify(boot, null,
        [
            At(600, "PM: suspend entry (s2idle)"),
            At(500, "PM: suspend exit"),
            At(5, "System is powering down.", "systemd-logind", 6, "journal"),
        ], [], [], []);
        Assert.Equal(ShutdownKind.PowerOff, boot.ShutdownKind);
    }

    [Fact]
    public void Pstore_in_next_boot_is_reported()
    {
        var boot = Boot();
        var next = new BootSession { BootId = "next", Index = 0, FirstEntry = End.AddMinutes(2), LastEntry = End.AddHours(1) };
        var pstore = TestData.Entry("Saved dmesg-efi-1234 to /var/lib/systemd/pstore", "systemd-pstore", 6, "journal", End.AddMinutes(3));
        Analyzer().Classify(boot, next, [At(5, "random")], [], [pstore], []);

        Assert.Equal(ShutdownKind.Unexpected, boot.ShutdownKind);
        Assert.Contains(boot.Reasons, r => r.Contains("pstore"));
    }

    private sealed class NullSource : IJournalSource
    {
        public string DisplayName => "null";
        public IAsyncEnumerable<LogEntry> QueryAsync(JournalQuery query, CancellationToken ct = default) => AsyncEnumerable.Empty<LogEntry>();
        public IAsyncEnumerable<LogEntry> FollowAsync(JournalQuery query, CancellationToken ct = default) => AsyncEnumerable.Empty<LogEntry>();
        public Task<IReadOnlyList<BootSession>> ListBootsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BootSession>>([]);
        public Task<IReadOnlyList<CrashReport>> GetCrashReportsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrashReport>>([]);
    }
}

[UseCulture("en-US")]
public class BootAnalyzerCrashTests
{
    [Fact]
    public void Program_crash_near_end_of_boot_is_listed()
    {
        var end = DateTimeOffset.Now;
        var boot = new BootSession { BootId = "b", Index = -1, FirstEntry = end.AddHours(-2), LastEntry = end };
        var crash = new CrashReport { Kind = CrashReportKind.Apport, Timestamp = end.AddSeconds(2), Title = "Crash: gnome-shell", Executable = "/usr/bin/gnome-shell" };
        var old = new CrashReport { Kind = CrashReportKind.Apport, Timestamp = end.AddHours(-1), Title = "Crash: x", Executable = "/usr/bin/x" };

        new BootAnalyzer(null!, TestData.Rules).Classify(boot, null,
            [TestData.Entry("System is powering down.", "systemd-logind", 6, "journal", end.AddSeconds(-3))], [], [], [crash, old]);

        Assert.Equal(ShutdownKind.PowerOff, boot.ShutdownKind);
        Assert.Contains(boot.Reasons, r => r.StartsWith("Program crash: gnome-shell", StringComparison.Ordinal));
        Assert.DoesNotContain(boot.Reasons, r => r.Contains(": x "));
    }
}
