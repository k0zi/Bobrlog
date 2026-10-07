using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Models;

public enum Severity
{
    Verbose = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

public static class SeverityMapper
{
    /// <summary>Maps a syslog PRIORITY (0 = emerg … 7 = debug) to a severity.</summary>
    public static Severity FromPriority(int priority) => priority switch
    {
        <= 2 => Severity.Critical,
        3 => Severity.Error,
        4 => Severity.Warning,
        5 or 6 => Severity.Info,
        _ => Severity.Verbose,
    };

    /// <summary>The most permissive journalctl priority that still includes the given minimum severity.</summary>
    public static int ToMaxPriority(Severity minimum) => minimum switch
    {
        Severity.Critical => 2,
        Severity.Error => 3,
        Severity.Warning => 4,
        Severity.Info => 6,
        _ => 7,
    };

    public static string DisplayName(Severity severity) => severity switch
    {
        Severity.Critical => CoreStrings.Severity_Critical,
        Severity.Error => CoreStrings.Severity_Error,
        Severity.Warning => CoreStrings.Severity_Warning,
        Severity.Info => CoreStrings.Severity_Info,
        _ => CoreStrings.Severity_Verbose,
    };
}
