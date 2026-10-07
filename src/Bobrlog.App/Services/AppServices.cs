using System.Diagnostics;
using Bobrlog.Core.Models;
using Bobrlog.Core.Platform;
using Bobrlog.Core.Rules;
using Bobrlog.Core.Sources;

namespace Bobrlog.App.Services;

/// <summary>Application-wide state shared by the pages.</summary>
public sealed class AppServices
{
    public RuleEngine Rules { get; } = RuleEngine.LoadDefault();
    public AppSettings Settings { get; } = AppSettings.Load();
    public PrivilegeInfo Privileges { get; } = PrivilegeInfo.Detect();

    public IJournalSource Source { get; private set; }

    /// <summary>Raised (on any thread) after the data source changed.</summary>
    public event EventHandler? SourceChanged;

    /// <summary>Cached boot list (newest first); analysis results are filled in lazily.</summary>
    public IReadOnlyList<BootSession>? Boots { get; set; }
    public IReadOnlyList<CrashReport>? Crashes { get; set; }

    public AppServices()
    {
        Source = new JournalctlSource(Rules);
        ApplyProcessPriority();
    }

    public async Task SelectSourceAsync()
    {
        var source = await SourceSelector.SelectAsync(Rules, Settings.SourceMode == SourceMode.Auto).ConfigureAwait(false);
        var changed = source.GetType() != Source.GetType();
        Source = source;
        if (changed)
        {
            Boots = null;
            Crashes = null;
            SourceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyProcessPriority()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            var target = Settings.LowPriority ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
            // Raising the priority back needs CAP_SYS_NICE; ignore the failure.
            if (self.PriorityClass != target)
                self.PriorityClass = target;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    public async Task<IReadOnlyList<BootSession>> GetBootsAsync(CancellationToken ct = default)
    {
        if (Boots is { } cached)
            return cached;
        var boots = await Source.ListBootsAsync(ct).ConfigureAwait(false);
        Boots = boots;
        return boots;
    }

    public async Task<IReadOnlyList<CrashReport>> GetCrashesAsync(CancellationToken ct = default)
    {
        if (Crashes is { } cached)
            return cached;
        var crashes = await Source.GetCrashReportsAsync(ct).ConfigureAwait(false);
        Crashes = crashes;
        return crashes;
    }
}
