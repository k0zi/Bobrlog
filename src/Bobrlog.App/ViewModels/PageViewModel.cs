using Avalonia.Media;

namespace Bobrlog.App.ViewModels;

public abstract class PageViewModel : ViewModelBase
{
    public abstract string Title { get; }
    public abstract Geometry Icon { get; }

    /// <summary>Called whenever the page becomes visible.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;

    /// <summary>Called when the page is navigated away from.</summary>
    public virtual void Deactivate()
    {
    }

    /// <summary>The data source switched (local ↔ service): cached data must be reloaded.</summary>
    public virtual void Invalidate()
    {
    }
}
