using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Models;

public enum ShutdownKind
{
    Unknown,
    Running,
    PowerOff,
    Reboot,
    Unexpected,
    SuspendNeverResumed,
}

public sealed class BootSession
{
    public required string BootId { get; init; }
    public int Index { get; init; }
    public DateTimeOffset FirstEntry { get; init; }
    public DateTimeOffset LastEntry { get; init; }

    public ShutdownKind ShutdownKind { get; set; } = ShutdownKind.Unknown;
    public List<string> Reasons { get; } = new();
    public List<LogEntry> Evidence { get; } = new();
    public bool Analyzed { get; set; }

    public TimeSpan Duration => LastEntry - FirstEntry;
    public bool IsCurrent => Index == 0;

    public static string DisplayName(ShutdownKind kind) => kind switch
    {
        ShutdownKind.Running => CoreStrings.Shutdown_Running,
        ShutdownKind.PowerOff => CoreStrings.Shutdown_PowerOff,
        ShutdownKind.Reboot => CoreStrings.Shutdown_Reboot,
        ShutdownKind.Unexpected => CoreStrings.Shutdown_Unexpected,
        ShutdownKind.SuspendNeverResumed => CoreStrings.Shutdown_SuspendNeverResumed,
        _ => CoreStrings.Shutdown_Unknown,
    };
}
