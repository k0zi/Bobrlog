using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Platform;

/// <summary>What the current process can see in the journal.</summary>
public sealed record PrivilegeInfo(bool IsRoot, uint Uid, IReadOnlyList<string> Groups, bool DmesgRestricted)
{
    private static readonly string[] JournalGroups = ["adm", "systemd-journal", "wheel"];

    public bool CanReadSystemJournal => IsRoot || Groups.Any(g => JournalGroups.Contains(g));

    public string? Warning =>
        CanReadSystemJournal
            ? null
            : CoreStrings.Privilege_Warning;

    public static PrivilegeInfo Detect()
    {
        var uid = Native.GetUid();
        var groups = Native.GetGroupNames();
        var dmesgRestricted = File.Exists("/proc/sys/kernel/dmesg_restrict") &&
                              SafeRead("/proc/sys/kernel/dmesg_restrict").Trim() == "1";
        return new PrivilegeInfo(uid == 0, uid, groups, dmesgRestricted);
    }

    private static string SafeRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
