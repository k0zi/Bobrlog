using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bobrlog.App.Controls;
using Bobrlog.App.Services;
using Bobrlog.Core.Analysis;
using Bobrlog.Core.Models;
using Bobrlog.Core.Sources;
using Bobrlog.App.Resources;

namespace Bobrlog.App.ViewModels;

public sealed record SeverityCount(Severity Severity, string Label, int Last24Hours, int Last7Days)
{
    public string Last7DaysText => string.Format(CultureInfo.CurrentCulture, Strings.Overview_Last7Days, Last7Days);
}

public sealed record DayBar(DateOnly Day, int Errors, int Warnings, double ErrorWidth, double WarningWidth)
{
    public string Label => Converters.Converters.Format(Day, Strings.Fmt_DayLabel);
    public string Tooltip => string.Format(CultureInfo.CurrentCulture, Strings.Overview_DayTooltip, Converters.Converters.Format(Day, Strings.Fmt_Date), Errors, Warnings);
}

public sealed record SourceCount(string Source, int Count, double Width);

/// <summary>Dashboard: what happened recently, which days were bad, why did the machine stop.</summary>
public sealed partial class OverviewViewModel(AppServices services, IShell shell) : PageViewModel
{
    public const double MaxBarWidth = 320;
    public const double MaxSourceBarWidth = 180;
    private const int Days = 14;
    private bool _loaded;

    public override string Title => Strings.Overview_Title;
    public override Geometry Icon => Icons.Overview;

    public BulkObservableCollection<SeverityCount> Counts { get; } = new();
    public BulkObservableCollection<DayBar> DayBars { get; } = new();
    public BulkObservableCollection<SourceCount> TopSources { get; } = new();
    public BulkObservableCollection<BootRow> RecentBoots { get; } = new();

    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string? ErrorText { get; set; }
    [ObservableProperty] public partial string Headline { get; set; } = "";
    [ObservableProperty] public partial string CrashSummary { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";

    public override async Task ActivateAsync()
    {
        if (!_loaded)
            await LoadAsync();
    }

    public override void Invalidate() => _loaded = false;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorText = null;
        StatusText = Strings.Overview_Summarizing;
        var source = services.Source;
        var now = DateTimeOffset.Now;
        try
        {
            var entries = await Task.Run(async () =>
            {
                var list = new List<(DateTimeOffset Time, Severity Severity, string Source)>();
                var query = new JournalQuery { Since = new DateTimeOffset(now.Date.AddDays(-(Days - 1))), MinSeverity = Severity.Warning, Limit = 200_000 };
                await foreach (var e in source.QueryAsync(query).ConfigureAwait(false))
                    list.Add((e.Timestamp, e.Severity, e.Unit is { } u && u != "init.scope" && !e.IsKernel ? u : e.Source));
                return list;
            });

            // Severity can be raised/lowered by rules, so count the classified severity.
            var day = now.AddDays(-1);
            var week = now.AddDays(-7);
            Counts.ReplaceAll(new[] { Severity.Critical, Severity.Error, Severity.Warning }.Select(s => new SeverityCount(
                s, SeverityMapper.DisplayName(s),
                entries.Count(e => e.Severity == s && e.Time >= day),
                entries.Count(e => e.Severity == s && e.Time >= week))));

            var perDay = Enumerable.Range(0, Days)
                .Select(i => DateOnly.FromDateTime(now.Date.AddDays(-i)))
                .Select(d => (Day: d,
                    Errors: entries.Count(e => e.Severity >= Severity.Error && DateOnly.FromDateTime(e.Time.LocalDateTime) == d),
                    Warnings: entries.Count(e => e.Severity == Severity.Warning && DateOnly.FromDateTime(e.Time.LocalDateTime) == d)))
                .ToList();
            var max = Math.Max(1, perDay.Max(d => Math.Max(d.Errors, d.Warnings)));
            DayBars.ReplaceAll(perDay.Select(d => new DayBar(d.Day, d.Errors, d.Warnings,
                Scale(d.Errors, max), Scale(d.Warnings, max))));

            var top = entries.Where(e => e.Severity >= Severity.Error && e.Time >= week)
                .GroupBy(e => e.Source).Select(g => (g.Key, Count: g.Count()))
                .OrderByDescending(g => g.Count).Take(8).ToList();
            var topMax = Math.Max(1, top.Count == 0 ? 1 : top[0].Count);
            TopSources.ReplaceAll(top.Select(t => new SourceCount(t.Key, t.Count, Scale(t.Count, topMax, MaxSourceBarWidth))));

            StatusText = Strings.Overview_AnalyzingBoots;
            var boots = await services.GetBootsAsync();
            var crashes = await services.GetCrashesAsync();
            var recent = boots.Take(8).ToList();
            var analyzer = new BootAnalyzer(source, services.Rules);
            await Task.Run(() => analyzer.AnalyzeAllAsync(recent.Where(b => !b.Analyzed).ToList(), crashes, maxBoots: 8));
            RecentBoots.ReplaceAll(recent.Select(b => new BootRow(b)));

            var unexpected = recent.Count(b => b.ShutdownKind is ShutdownKind.Unexpected or ShutdownKind.SuspendNeverResumed);
            var errors24 = Counts.Where(c => c.Severity >= Severity.Error).Sum(c => c.Last24Hours);
            Headline = unexpected > 0
                ? string.Format(CultureInfo.CurrentCulture, Strings.Overview_HeadlineUnexpected, recent.Count, unexpected)
                : errors24 > 0
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Overview_HeadlineErrors, errors24)
                    : Strings.Overview_HeadlineClean;
            var recentCrashes = crashes.Count(c => c.Timestamp >= now.AddDays(-30));
            CrashSummary = recentCrashes == 0
                ? Strings.Overview_NoCrashes
                : string.Format(CultureInfo.CurrentCulture, Strings.Overview_Crashes, recentCrashes);
            StatusText = string.Format(CultureInfo.CurrentCulture, Strings.Overview_Status, entries.Count, Days);
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

    private static double Scale(int value, int max, double width = MaxBarWidth) => value == 0 ? 0 : Math.Max(3, width * value / max);

    [RelayCommand]
    private void OpenDay(DayBar bar) =>
        shell.ShowEvents(new EventFilterPreset { Day = bar.Day, MinSeverity = Severity.Warning });

    [RelayCommand]
    private void OpenSource(SourceCount item) =>
        shell.ShowEvents(new EventFilterPreset { Source = item.Source, MinSeverity = Severity.Error });

    [RelayCommand]
    private void OpenBoot(BootRow row) => shell.ShowBoots(row.Boot.BootId);
}
