using Bobrlog.Core.Models;

namespace Bobrlog.Core.Tests;

public class RuleEngineTests
{
    [Theory]
    [InlineData("Kernel panic - not syncing: Fatal exception", "kernel-panic", EventCategory.Crashes, Severity.Critical)]
    [InlineData("watchdog: BUG: soft lockup - CPU#3 stuck for 22s! [kworker:123]", "soft-lockup", EventCategory.Crashes, Severity.Critical)]
    [InlineData("Out of memory: Killed process 1234 (firefox) total-vm:123kB", "oom-kill", EventCategory.Crashes, Severity.Error)]
    [InlineData("traps: fprintd[22124] general protection fault ip:73dd5eab5377 sp:7ff error:0 in libc.so.6", "segfault", EventCategory.Crashes, Severity.Warning)]
    [InlineData("inspircd[2677]: segfault at 80 ip 000077e5290ddb64 sp 00007ffe1bc12698 error 4", "segfault", EventCategory.Crashes, Severity.Warning)]
    [InlineData("amdgpu 0000:c6:00.0: GPU reset begin!. Source:  1", "gpu-hang", EventCategory.KernelHardware, Severity.Error)]
    [InlineData("mce: [Hardware Error]: Machine check events logged", "mce", EventCategory.KernelHardware, Severity.Error)]
    [InlineData("pcieport 0000:00:01.1: AER: Corrected error received: 0000:01:00.0", "pcie-aer", EventCategory.KernelHardware, Severity.Warning)]
    [InlineData("nvme nvme0: I/O 12 QID 3 timeout, reset controller", "disk-io", EventCategory.KernelHardware, Severity.Error)]
    [InlineData("PM: suspend entry (s2idle)", "suspend", EventCategory.Power, Severity.Info)]
    [InlineData("logitech-hidpp-device 0005:046D:B023.000A: HID++ 4.5 device connected.", "kernel-generic", EventCategory.KernelHardware, Severity.Info)]
    public void Classifies_kernel_messages(string message, string ruleId, EventCategory category, Severity severity)
    {
        var e = TestData.Entry(message);
        Assert.Equal(ruleId, e.RuleId);
        Assert.Equal(category, e.Category);
        Assert.Equal(severity, e.Severity);
    }

    [Fact]
    public void Boot_time_mce_info_is_not_flagged_as_hardware_error()
    {
        var e = TestData.Entry("mce: CPU0: Thermal monitoring enabled (TM1)");
        Assert.NotEqual("mce", e.RuleId);
        Assert.Equal(Severity.Info, e.Severity);
    }

    [Fact]
    public void Unit_failure_is_raised_to_warning()
    {
        var e = TestData.Entry("mpris-proxy.service: Main process exited, code=exited, status=1/FAILURE", "systemd", 5, "journal");
        Assert.Equal("unit-failed", e.RuleId);
        Assert.Equal(EventCategory.Services, e.Category);
        Assert.Equal(Severity.Warning, e.Severity);
    }

    [Fact]
    public void Successful_exit_is_not_a_failure()
    {
        var e = TestData.Entry("foo.service: Main process exited, code=exited, status=0/SUCCESS", "systemd", 6, "journal");
        Assert.Equal("systemd-generic", e.RuleId);
        Assert.Equal(Severity.Info, e.Severity);
    }

    [Fact]
    public void Libvirt_guest_suspend_service_is_not_system_suspend()
    {
        var e = TestData.Entry("Starting libvirt-guests.service - libvirt guests suspend/resume service...", "systemd", 6, "journal");
        Assert.NotEqual("suspend", e.RuleId);
    }

    [Theory]
    [InlineData("sudo", "pam_unix(sudo:auth): authentication failure; logname=x", "auth-failure", EventCategory.Security)]
    [InlineData("sudo", "k0zi : TTY=pts/0 ; PWD=/ ; USER=root ; COMMAND=/bin/ls", "security-generic", EventCategory.Security)]
    [InlineData("NetworkManager", "device (wlp1s0): state change", "network", EventCategory.Network)]
    [InlineData("gnome-shell", "JS ERROR: something", "desktop", EventCategory.Applications)]
    public void Classifies_userspace_by_identifier(string identifier, string message, string ruleId, EventCategory category)
    {
        var e = TestData.Entry(message, identifier, 6, "syslog");
        Assert.Equal(ruleId, e.RuleId);
        Assert.Equal(category, e.Category);
    }

    [Fact]
    public void Unknown_userspace_falls_back_to_other()
    {
        var e = TestData.Entry("hello", "some-daemon", 6, "syslog");
        Assert.Null(e.RuleId);
        Assert.Equal(EventCategory.Other, e.Category);
    }

    [Fact]
    public void All_default_rules_compile_and_have_unique_ids()
    {
        var ids = TestData.Rules.Rules.Select(r => r.Id).ToList();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
