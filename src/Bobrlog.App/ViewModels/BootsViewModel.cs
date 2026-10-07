using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bobrlog.App.Controls;
using Bobrlog.App.Services;
using Bobrlog.Core.Analysis;
using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;
using System.Globalization;
using Bobrlog.App.Resources;

namespace Bobrlog.App.ViewModels;

public sealed partial class BootRow(BootSession boot) : ObservableObject
{
    public BootSession Boot { get; } = boot;
    public ShutdownKind Kind => Boot.ShutdownKind;
    public string Started => Converters.Converters.Format(Boot.FirstEntry, Strings.Fmt_DateTimeMinutes);
    public string Ended => Boot.IsCurrent ? "–" : Converters.Converters.Format(Boot.LastEntry, Strings.Fmt_DateTimeMinutes);
    public string Duration => Converters.Converters.FormatDuration(Boot.Duration);
    public string KindText => Boot.Analyzed ? BootSession.DisplayName(Boot.ShutdownKind) : Strings.Boots_Analyzing;
    public string Summary => Boot.Reasons.FirstOrDefault() ?? "";
    public string IndexText => Boot.Index == 0 ? Strings.Boots_CurrentIndex : Boot.Index.ToString(CultureInfo.CurrentCulture);

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>"Why did my machine stop/restart?" – one row per boot with the analysed shutdown cause.</summary>
public sealed partial class BootsViewModel(AppServices services, IShell shell) : PageViewModel
{
    private bool _loaded;
    private string? _pendingSelection;

    public override string Title => Strings.Boots_Title;
    public override Geometry Icon => Icons.Power;

    public BulkObservableCollection<BootRow> Rows { get; } = new();
    public BulkObservableCollection<string> Reasons { get; } = new();
    public BulkObservableCollection<LogEntry> Evidence { get; } = new();

    [ObservableProperty] public partial BootRow? SelectedRow { get; set; }
    [ObservableProperty] public partial LogEntry? SelectedEvidence { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial string? ErrorText { get; set; }

    public string SelectedTitle => SelectedRow is { } r
        ? string.Format(CultureInfo.CurrentCulture, Strings.Boots_SelectedTitle, r.KindText, r.Started, r.Ended, r.Duration)
        : Strings.Boots_SelectPrompt;

    public void Select(string? bootId)
    {
        _pendingSelection = bootId;
        if (bootId is not null && Rows.FirstOrDefault(r => r.Boot.BootId == bootId) is { } row)
            SelectedRow = row;
    }

    public override async Task ActivateAsync()
    {
        if (!_loaded)
            await LoadAsync();
    }

    public override void Invalidate() => _loaded = false;

    partial void OnSelectedRowChanged(BootRow? value) => UpdateDetails();

    private void SelectRowLater() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            SelectedRow = Rows.FirstOrDefault(r => r.Boot.BootId == _pendingSelection) ?? SelectedRow ?? Rows.FirstOrDefault();
            _pendingSelection = null;
        }, Avalonia.Threading.DispatcherPriority.Background);

    private void UpdateDetails()
    {
        Reasons.ReplaceAll(SelectedRow?.Boot.Reasons ?? []);
        Evidence.ReplaceAll(SelectedRow?.Boot.Evidence ?? []);
        OnPropertyChanged(nameof(SelectedTitle));
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = null;
        StatusText = Strings.Boots_Listing;
        try
        {
            services.Boots = null;
            services.Crashes = null;
            var boots = await services.GetBootsAsync();
            var crashes = await services.GetCrashesAsync();
            Rows.ReplaceAll(boots.Select(b => new BootRow(b)));
            SelectRowLater();

            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Boots_AnalyzingCount, boots.Count);
            var analyzer = new BootAnalyzer(services.Source, services.Rules);
            await Task.Run(() => analyzer.AnalyzeAllAsync(boots, crashes, maxBoots: 50));
            foreach (var row in Rows)
                row.Refresh();
            UpdateDetails();
            SelectRowLater();

            var unexpected = boots.Count(b => b.ShutdownKind is ShutdownKind.Unexpected or ShutdownKind.SuspendNeverResumed);
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Boots_Status, boots.Count, unexpected);
            _loaded = true;
        }
        catch (Exception ex) when (ex is JournalSourceException or InvalidOperationException or IOException
                                       or System.Net.Sockets.SocketException)
        {
            ErrorText = ex.Message;
            StatusText = "";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ShowAllEvents()
    {
        if (SelectedRow is { } r)
            shell.ShowEvents(new EventFilterPreset { BootId = r.Boot.BootId, MinSeverity = Severity.Info });
    }

    [RelayCommand]
    private void ShowProblems()
    {
        if (SelectedRow is { } r)
            shell.ShowEvents(new EventFilterPreset { BootId = r.Boot.BootId, MinSeverity = Severity.Warning });
    }
}
