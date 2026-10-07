using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bobrlog.App.Resources;
using Bobrlog.App.Services;
using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;

namespace Bobrlog.App.ViewModels;

public enum TimeRangeKind
{
    LastHour,
    Today,
    Last24Hours,
    Last7Days,
    Last30Days,
    Day,
    Boot,
    All,
}

public sealed record Option<T>(string Label, T Value)
{
    public override string ToString() => Label;
}

public sealed record DetailField(string Name, string Value);

public sealed record BootOption(string Label, string? BootId)
{
    public override string ToString() => Label;
}

/// <summary>Event list for one category (or all events) with filters, paging, details and live follow.</summary>
public sealed partial class EventListViewModel : PageViewModel
{
    private const int BatchSize = 5000;
    private const int MaxScanPerLoad = 150_000;

    private readonly AppServices _services;
    private readonly IShell _shell;
    private readonly string _title;
    private readonly Geometry _icon;
    private readonly string? _prefilter;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _followCts;
    private string? _pageCursor;
    private bool _loadedOnce;
    private bool _suppressReload;
    private DispatcherTimer? _searchDebounce;

    public EventListViewModel(AppServices services, IShell shell, EventCategory? category, string title, Geometry icon,
        Severity defaultMinSeverity = Severity.Info, TimeRangeKind defaultRange = TimeRangeKind.Last7Days,
        string? prefilter = null)
    {
        _prefilter = prefilter;
        _services = services;
        _shell = shell;
        Category = category;
        _title = title;
        _icon = icon;
        _suppressReload = true;
        SelectedRange = Ranges.First(r => r.Value == defaultRange);
        SelectedSeverity = Severities.First(s => s.Value == defaultMinSeverity);
        SelectedBoot = Boots[0];
        _suppressReload = false;
    }

    public override string Title => _title;
    public override Geometry Icon => _icon;
    public EventCategory? Category { get; }

    public IReadOnlyList<Option<TimeRangeKind>> Ranges { get; } =
    [
        new(Strings.Range_LastHour, TimeRangeKind.LastHour),
        new(Strings.Range_Today, TimeRangeKind.Today),
        new(Strings.Range_Last24Hours, TimeRangeKind.Last24Hours),
        new(Strings.Range_Last7Days, TimeRangeKind.Last7Days),
        new(Strings.Range_Last30Days, TimeRangeKind.Last30Days),
        new(Strings.Range_Day, TimeRangeKind.Day),
        new(Strings.Range_Boot, TimeRangeKind.Boot),
        new(Strings.Range_All, TimeRangeKind.All),
    ];

    public IReadOnlyList<Option<Severity>> Severities { get; } =
    [
        new(SeverityMapper.DisplayName(Severity.Critical), Severity.Critical),
        new(Strings.SeverityFilter_Error, Severity.Error),
        new(Strings.SeverityFilter_Warning, Severity.Warning),
        new(Strings.SeverityFilter_Info, Severity.Info),
        new(Strings.SeverityFilter_All, Severity.Verbose),
    ];

    public BulkObservableCollection<BootOption> Boots { get; } = new() { new BootOption(Strings.EventList_CurrentBoot, "0") };

    public BulkObservableCollection<LogEntry> Entries { get; } = new();
    public BulkObservableCollection<DetailField> DetailFields { get; } = new();

    [ObservableProperty] public partial Option<TimeRangeKind> SelectedRange { get; set; }
    [ObservableProperty] public partial Option<Severity> SelectedSeverity { get; set; }
    [ObservableProperty] public partial DateTimeOffset? SelectedDay { get; set; } = DateTimeOffset.Now.Date;
    [ObservableProperty] public partial BootOption SelectedBoot { get; set; }
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SourceFilter { get; set; } = string.Empty;

    [ObservableProperty] public partial LogEntry? SelectedEntry { get; set; }
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial bool HasMore { get; set; }
    [ObservableProperty] public partial bool IsFollowing { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string? ErrorText { get; set; }

    public bool IsDayRange => SelectedRange.Value == TimeRangeKind.Day;
    public bool IsBootRange => SelectedRange.Value == TimeRangeKind.Boot;
    public bool HasSelection => SelectedEntry is not null;

    public string DetailHeader => SelectedEntry is { } e
        ? $"{SeverityMapper.DisplayName(e.Severity)} • {Converters.Converters.Format(e.Timestamp, Strings.Fmt_DateTimeMillis)} • {e.Source}" +
          (e.Pid is { } pid ? $"[{pid}]" : "")
        : string.Empty;

    public string DetailCategory => SelectedEntry is { } e ? EventCategoryInfo.DisplayName(e.Category) + (e.RuleId is { } r ? "  " + string.Format(CultureInfo.CurrentCulture, Strings.EventList_Rule, r) : "") : "";

    public void ApplyPreset(EventFilterPreset preset)
    {
        _suppressReload = true;
        if (preset.Day is { } day)
        {
            SelectedRange = Ranges.First(r => r.Value == TimeRangeKind.Day);
            SelectedDay = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue));
        }
        if (preset.BootId is { } bootId)
        {
            SelectedRange = Ranges.First(r => r.Value == TimeRangeKind.Boot);
            var option = Boots.FirstOrDefault(b => b.BootId == bootId);
            if (option is null)
            {
                option = new BootOption(string.Format(CultureInfo.CurrentCulture, Strings.EventList_BootShort, bootId[..8]), bootId);
                Boots.Add(option);
            }
            SelectedBoot = option;
        }
        if (preset.MinSeverity is { } sev)
            SelectedSeverity = Severities.First(s => s.Value == sev);
        SourceFilter = preset.Source ?? string.Empty;
        _suppressReload = false;
        _loadedOnce = false;
    }

    public override async Task ActivateAsync()
    {
        _ = LoadBootOptionsAsync();
        if (!_loadedOnce)
            await ReloadAsync();
    }

    public override void Deactivate() => StopFollow();

    public override void Invalidate()
    {
        _loadedOnce = false;
        StopFollow();
    }

    partial void OnSelectedRangeChanged(Option<TimeRangeKind> value)
    {
        OnPropertyChanged(nameof(IsDayRange));
        OnPropertyChanged(nameof(IsBootRange));
        ScheduleReload();
    }

    partial void OnSelectedSeverityChanged(Option<Severity> value) => ScheduleReload();
    partial void OnSelectedDayChanged(DateTimeOffset? value) => ScheduleReload();
    partial void OnSelectedBootChanged(BootOption value) => ScheduleReload();
    partial void OnSearchTextChanged(string value) => ScheduleReload(debounce: true);
    partial void OnSourceFilterChanged(string value) => ScheduleReload(debounce: true);

    partial void OnSelectedEntryChanged(LogEntry? value)
    {
        DetailFields.ReplaceAll(value is null
            ? []
            : value.Fields.Where(f => f.Key is not "MESSAGE")
                .OrderBy(f => f.Key.StartsWith('_') ? 1 : 0).ThenBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new DetailField(f.Key, f.Value)));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DetailHeader));
        OnPropertyChanged(nameof(DetailCategory));
    }

    private void ScheduleReload(bool debounce = false)
    {
        if (_suppressReload)
            return;
        if (!debounce)
        {
            _ = ReloadAsync();
            return;
        }
        _searchDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) =>
        {
            _searchDebounce!.Stop();
            _ = ReloadAsync();
        });
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async Task LoadBootOptionsAsync()
    {
        try
        {
            var boots = await _services.GetBootsAsync();
            var selectedId = SelectedBoot?.BootId;
            var options = boots.Select(b => new BootOption(
                b.IsCurrent
                    ? string.Format(CultureInfo.CurrentCulture, Strings.EventList_CurrentBootSince, Converters.Converters.Format(b.FirstEntry, Strings.Fmt_ShortDateTime))
                    : $"{Converters.Converters.Format(b.FirstEntry, Strings.Fmt_DateTimeMinutes)} – {Converters.Converters.Format(b.LastEntry, Strings.Fmt_ShortDateTime)}",
                b.BootId)).ToList();
            _suppressReload = true;
            Boots.ReplaceAll(options);
            SelectedBoot = options.FirstOrDefault(o => o.BootId == selectedId)
                           ?? (selectedId == "0" ? options.FirstOrDefault() : null)
                           ?? options.FirstOrDefault() ?? new BootOption(Strings.EventList_CurrentBoot, "0");
            _suppressReload = false;
        }
        catch (Exception ex) when (ex is JournalSourceException or InvalidOperationException or IOException)
        {
            _suppressReload = false;
        }
    }

    private JournalQuery BuildQuery()
    {
        var now = DateTimeOffset.Now;
        var query = new JournalQuery { MinSeverity = SelectedSeverity.Value == Severity.Verbose ? null : SelectedSeverity.Value };
        query = SelectedRange.Value switch
        {
            TimeRangeKind.LastHour => query with { Since = now.AddHours(-1) },
            TimeRangeKind.Today => query with { Since = new DateTimeOffset(now.Date) },
            TimeRangeKind.Last24Hours => query with { Since = now.AddDays(-1) },
            TimeRangeKind.Last7Days => query with { Since = now.AddDays(-7) },
            TimeRangeKind.Last30Days => query with { Since = now.AddDays(-30) },
            TimeRangeKind.Day => SelectedDay is { } d
                ? query with { Since = new DateTimeOffset(d.Date), Until = new DateTimeOffset(d.Date.AddDays(1)) }
                : query,
            TimeRangeKind.Boot => query with { BootId = SelectedBoot?.BootId ?? "0" },
            _ => query,
        };

        // A category may narrow the search on the journalctl side (much faster than scanning everything);
        // the rule engine still decides the final category on the client.
        if (!string.IsNullOrWhiteSpace(SearchText))
            query = query with { Grep = SearchText.Trim() };
        else if (_prefilter is not null)
            query = query with { Grep = _prefilter };

        var src = SourceFilter.Trim();
        if (src.Length > 0)
        {
            query = src.Contains('.') && (src.EndsWith(".service", StringComparison.Ordinal) || src.EndsWith(".scope", StringComparison.Ordinal)
                                          || src.EndsWith(".timer", StringComparison.Ordinal) || src.EndsWith(".mount", StringComparison.Ordinal))
                ? query with { Unit = src }
                : query with { Identifier = src };
        }

        return query;
    }

    private bool Matches(LogEntry e) => Category is null || e.Category == Category;

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync(append: false);

    [RelayCommand]
    private Task LoadMoreAsync() => LoadAsync(append: true);

    private async Task LoadAsync(bool append)
    {
        if (_loadCts is not null)
            await _loadCts.CancelAsync();
        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var ct = cts.Token;

        if (!append)
        {
            StopFollow();
            _pageCursor = null;
        }

        var baseQuery = BuildQuery();
        var pageSize = _services.Settings.PageSize;
        var source = _services.Source;
        var cursor = append ? _pageCursor : null;

        IsLoading = true;
        ErrorText = null;
        StatusText = Strings.EventList_Loading;
        var started = DateTime.UtcNow;

        try
        {
            var progress = new Progress<int>(scanned => StatusText = string.Format(CultureInfo.CurrentCulture, Strings.EventList_LoadingProgress, scanned));
            var (found, lastCursor, exhausted, scanned) = await Task.Run(async () =>
            {
                var result = new List<LogEntry>();
                var c = cursor;
                var total = 0;
                var done = false;
                IProgress<int> p = progress;
                while (result.Count < pageSize && total < MaxScanPerLoad && !ct.IsCancellationRequested)
                {
                    var batch = Category is null ? pageSize - result.Count : BatchSize;
                    var q = baseQuery with { AfterCursor = c, Limit = batch, NewestFirst = true };
                    var n = 0;
                    await foreach (var e in source.QueryAsync(q, ct).ConfigureAwait(false))
                    {
                        n++;
                        c = e.Cursor ?? c;
                        if (Matches(e))
                            result.Add(e);
                    }
                    total += n;
                    p.Report(total);
                    if (n < batch)
                    {
                        done = true;
                        break;
                    }
                }
                return (result, c, done, total);
            }, ct);

            if (ct.IsCancellationRequested)
                return;

            _pageCursor = lastCursor;
            HasMore = !exhausted;
            if (append)
                Entries.AddRange(found);
            else
            {
                Entries.ReplaceAll(found);
                // The DataGrid clears its selection while handling the reset; select afterwards.
                Dispatcher.UIThread.Post(() => SelectedEntry ??= Entries.FirstOrDefault(), DispatcherPriority.Background);
            }

            _loadedOnce = true;
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;
            var parts = new List<string> { string.Format(CultureInfo.CurrentCulture, Strings.EventList_StatusEvents, Entries.Count) };
            if (Category is not null)
                parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.EventList_StatusScanned, scanned));
            parts.Add(string.Format(CultureInfo.CurrentCulture, Strings.Duration_Seconds, elapsed.ToString("0.0", CultureInfo.CurrentCulture)));
            if (HasMore)
                parts.Add(Strings.EventList_StatusMore);
            StatusText = string.Join(" • ", parts);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is JournalSourceException or InvalidOperationException or IOException
                                       or System.Net.Sockets.SocketException)
        {
            ErrorText = ex.Message;
            StatusText = Strings.EventList_LoadError;
        }
        finally
        {
            if (_loadCts == cts)
                IsLoading = false;
        }
    }

    partial void OnIsFollowingChanged(bool value)
    {
        if (value)
            StartFollow();
        else
            StopFollow();
    }

    private void StartFollow()
    {
        _followCts?.Cancel();
        var cts = new CancellationTokenSource();
        _followCts = cts;
        var query = BuildQuery() with { BootId = null };
        var source = _services.Source;

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var e in source.FollowAsync(query, cts.Token).ConfigureAwait(false))
                {
                    if (!Matches(e))
                        continue;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!cts.IsCancellationRequested)
                            Entries.Insert(0, e);
                    });
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    ErrorText = string.Format(CultureInfo.CurrentCulture, Strings.EventList_FollowStopped, ex.Message);
                    IsFollowing = false;
                });
            }
        });
    }

    private void StopFollow()
    {
        _followCts?.Cancel();
        _followCts = null;
        if (IsFollowing)
            IsFollowing = false;
    }

    [RelayCommand]
    private Task CopyMessageAsync() =>
        SelectedEntry is { } e
            ? _shell.CopyToClipboardAsync($"{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff} {e.Source}{(e.Pid is { } p ? $"[{p}]" : "")}: {e.Message}")
            : Task.CompletedTask;

    [RelayCommand]
    private Task CopyCommandAsync() =>
        _shell.CopyToClipboardAsync(JournalctlArguments.ToCommandLine(BuildQuery() with { Limit = null }));

    [RelayCommand]
    private void ShowBoot()
    {
        if (SelectedEntry?.BootId is { } id)
            _shell.ShowBoots(id);
    }

    [RelayCommand]
    private void FilterBySource()
    {
        if (SelectedEntry is { } e)
            SourceFilter = e.Unit is { } u && !e.IsKernel && u != "init.scope" ? u : e.Identifier ?? e.Source;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressReload = true;
        SearchText = string.Empty;
        SourceFilter = string.Empty;
        _suppressReload = false;
        _ = ReloadAsync();
    }
}
