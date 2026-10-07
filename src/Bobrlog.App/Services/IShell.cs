using Bobrlog.Core.Models;

namespace Bobrlog.App.Services;

/// <summary>Navigation and platform services the pages need from the main window.</summary>
public interface IShell
{
    void ShowEvents(EventFilterPreset preset);
    void ShowBoots(string? bootId = null);
    Task CopyToClipboardAsync(string text);
}

/// <summary>Initial filter when navigating to an event list from another page.</summary>
public sealed record EventFilterPreset
{
    public EventCategory? Category { get; init; }
    public DateOnly? Day { get; init; }
    public string? BootId { get; init; }
    public Severity? MinSeverity { get; init; }
    public string? Source { get; init; }
}
