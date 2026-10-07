using System.Runtime.InteropServices;

namespace Bobrlog.Core.Platform;

public static partial class Native
{
    [LibraryImport("libc", EntryPoint = "getuid")]
    internal static partial uint GetUid();

    public static uint CurrentUid => GetUid();

    [LibraryImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static partial int GetGroups(int size, [Out] uint[] list);

    [LibraryImport("libc", EntryPoint = "getgid")]
    private static partial uint GetGid();

    internal static IReadOnlyList<string> GetGroupNames()
    {
        var count = GetGroups(0, []);
        var gids = new uint[Math.Max(count, 0) + 1];
        count = GetGroups(gids.Length - 1, gids);
        gids[^1] = GetGid();
        var ids = gids.Take(Math.Max(count, 0)).Append(gids[^1]).ToHashSet();

        // Map ids to names via /etc/group (NSS groups from LDAP etc. are rare on desktops).
        var names = new List<string>();
        try
        {
            foreach (var line in File.ReadLines("/etc/group"))
            {
                var parts = line.Split(':');
                if (parts.Length >= 3 && uint.TryParse(parts[2], out var gid) && ids.Contains(gid))
                    names.Add(parts[0]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return names;
    }
}
