using Bobrlog.Core.Resources;

namespace Bobrlog.Core.Models;

public enum EventCategory
{
    Other = 0,
    KernelHardware,
    Crashes,
    Services,
    Power,
    Security,
    Network,
    Applications,
}

public static class EventCategoryInfo
{
    public static string DisplayName(EventCategory category) => category switch
    {
        EventCategory.KernelHardware => CoreStrings.Category_KernelHardware,
        EventCategory.Crashes => CoreStrings.Category_Crashes,
        EventCategory.Services => CoreStrings.Category_Services,
        EventCategory.Power => CoreStrings.Category_Power,
        EventCategory.Security => CoreStrings.Category_Security,
        EventCategory.Network => CoreStrings.Category_Network,
        EventCategory.Applications => CoreStrings.Category_Applications,
        _ => CoreStrings.Category_Other,
    };
}
