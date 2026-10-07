using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bobrlog.App.Controls;
using Bobrlog.App.Services;
using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;
using System.Globalization;
using Bobrlog.App.Resources;

namespace Bobrlog.App.ViewModels;

/// <summary>Crash artefacts outside the journal: apport, coredumps, pstore.</summary>
public sealed partial class CrashesViewModel(AppServices services, IShell shell) : PageViewModel
{
    private bool _loaded;

    public override string Title => Strings.Crashes_Title;
    public override Geometry Icon => Icons.FileAlert;

    public BulkObservableCollection<CrashReport> Reports { get; } = new();

    [ObservableProperty] public partial CrashReport? SelectedReport { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string? ErrorText { get; set; }

    public string Hint => services.Privileges.IsRoot || services.Source is Core.Service.ServiceJournalSource
        ? Strings.Crashes_HintFull
        : Strings.Crashes_HintLimited;

    public override async Task ActivateAsync()
    {
        if (!_loaded)
            await LoadAsync();
    }

    public override void Invalidate()
    {
        _loaded = false;
        OnPropertyChanged(nameof(Hint));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = null;
        try
        {
            services.Crashes = null;
            var reports = await services.GetCrashesAsync();
            Reports.ReplaceAll(reports);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedReport ??= Reports.FirstOrDefault(),
                Avalonia.Threading.DispatcherPriority.Background);
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Crashes_Status, reports.Count);
            _loaded = true;
        }
        catch (Exception ex) when (ex is JournalSourceException or InvalidOperationException or IOException
                                       or System.Net.Sockets.SocketException)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ShowEventsAround()
    {
        if (SelectedReport is { } r)
            shell.ShowEvents(new EventFilterPreset { Day = DateOnly.FromDateTime(r.Timestamp.LocalDateTime), MinSeverity = Severity.Warning });
    }

    [RelayCommand]
    private Task CopyDetailsAsync() =>
        SelectedReport is { } r
            ? shell.CopyToClipboardAsync($"{r.Title}\n{r.Timestamp:yyyy-MM-dd HH:mm:ss}\n{r.Path}\n{r.Details}")
            : Task.CompletedTask;
}
