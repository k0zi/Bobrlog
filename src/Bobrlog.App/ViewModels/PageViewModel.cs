using Avalonia.Media;

namespace Bobrlog.App.ViewModels;

/// <summary>Entry of the side menu: a page or a group of pages.</summary>
public abstract class NavItem : ViewModelBase
{
    public abstract string Title { get; }
    public abstract Geometry Icon { get; }

    /// <summary>Sub items; empty for a page.</summary>
    public virtual IReadOnlyList<NavItem> Children => [];

    /// <summary>Sub items are visible (groups only).</summary>
    public bool IsExpanded { get; set; }
}

/// <summary>Side menu parent that only expands / collapses its pages.</summary>
public sealed class NavGroup(string title, Geometry icon, IReadOnlyList<NavItem> children) : NavItem
{
    public override string Title => title;
    public override Geometry Icon => icon;
    public override IReadOnlyList<NavItem> Children => children;
}

public abstract class PageViewModel : NavItem
{
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
