using Avalonia.Controls;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Bobrlog.App.Controls;
using Bobrlog.App.Services;
using Bobrlog.Core.Models;
using System.Globalization;
using Bobrlog.App.Resources;

namespace Bobrlog.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IShell
{
    private readonly AppServices _services;
    private readonly TopLevel _topLevel;
    private readonly EventListViewModel _allEvents;
    private readonly BootsViewModel _boots;
    private readonly Dictionary<EventCategory, EventListViewModel> _categoryPages = new();

    public MainWindowViewModel(AppServices services, TopLevel topLevel)
    {
        _services = services;
        _topLevel = topLevel;

        _boots = new BootsViewModel(services, this);
        _allEvents = new EventListViewModel(services, this, null, Strings.Common_AllEvents, Icons.List);

        EventListViewModel Category(EventCategory c, Avalonia.Media.Geometry icon, Severity min, string? prefilter = null) =>
            _categoryPages[c] = new EventListViewModel(services, this, c, EventCategoryInfo.DisplayName(c), icon, min, prefilter: prefilter);

        Pages =
        [
            new OverviewViewModel(services, this),
            _boots,
            new CrashesViewModel(services, this),
            Category(EventCategory.Crashes, Icons.Flash, Severity.Info, CrashPrefilter),
            Category(EventCategory.KernelHardware, Icons.Chip, Severity.Warning),
            Category(EventCategory.Services, Icons.Cog, Severity.Warning),
            Category(EventCategory.Power, Icons.Battery, Severity.Info),
            Category(EventCategory.Security, Icons.Shield, Severity.Info),
            Category(EventCategory.Network, Icons.Network, Severity.Warning),
            Category(EventCategory.Applications, Icons.Window, Severity.Warning),
            Category(EventCategory.Other, Icons.Dots, Severity.Warning),
            _allEvents,
            new SettingsViewModel(services, SelectSourceAsync),
        ];

        SelectedPage = Pages[0];
        _ = InitializeAsync();
    }

    /// <summary>Server-side pre-filter for the crash category (keywords of the crash rules).</summary>
    private const string CrashPrefilter =
        "panic|oops|BUG|lockup|stall|hung_task|blocked for more than|out of memory|oom|segfault|general protection|" +
        "trap|dumped core|core-dump|terminated abnormally|pstore|watchdog|signal";

    public IReadOnlyList<PageViewModel> Pages { get; }

    [ObservableProperty] public partial PageViewModel? SelectedPage { get; set; }
    [ObservableProperty] public partial string SourceText { get; set; } = "";

    public string? PrivilegeWarning => _services.Privileges.Warning;
    public bool HasPrivilegeWarning => PrivilegeWarning is not null;

    private async Task InitializeAsync()
    {
        await SelectSourceAsync();
    }

    private async Task SelectSourceAsync()
    {
        var before = _services.Source;
        await _services.SelectSourceAsync();
        SourceText = string.Format(CultureInfo.CurrentCulture, Strings.Main_SourceText, _services.Source.DisplayName) + (_services.Privileges.IsRoot ? " (root)" : "");
        if (!ReferenceEquals(before, _services.Source))
        {
            foreach (var page in Pages)
                page.Invalidate();
        }
        if (SelectedPage is not null)
            await SelectedPage.ActivateAsync();
    }

    partial void OnSelectedPageChanged(PageViewModel? oldValue, PageViewModel? newValue)
    {
        oldValue?.Deactivate();
        if (newValue is not null)
            _ = newValue.ActivateAsync();
    }

    public void ShowEvents(EventFilterPreset preset)
    {
        var page = preset.Category is { } c && _categoryPages.TryGetValue(c, out var p) ? p : _allEvents;
        page.ApplyPreset(preset);
        if (ReferenceEquals(SelectedPage, page))
            _ = page.ActivateAsync();
        else
            SelectedPage = page;
    }

    public void ShowBoots(string? bootId = null)
    {
        _boots.Select(bootId);
        SelectedPage = _boots;
    }

    public async Task CopyToClipboardAsync(string text)
    {
        if (_topLevel.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }
}
