using Avalonia;
using Avalonia.Controls;
using Bobrlog.Core.Models;

namespace Bobrlog.App.Controls;

/// <summary>Icon for a severity; colour comes from the "severity-*" style classes (see App.axaml).</summary>
public sealed class SeverityIcon : PathIcon
{
    public static readonly StyledProperty<Severity> SeverityProperty =
        AvaloniaProperty.Register<SeverityIcon, Severity>(nameof(Severity), Severity.Info);

    static SeverityIcon()
    {
        SeverityProperty.Changed.AddClassHandler<SeverityIcon>((icon, _) => icon.Update());
    }

    public SeverityIcon()
    {
        Width = 16;
        Height = 16;
        Update();
    }

    protected override Type StyleKeyOverride => typeof(PathIcon);

    public Severity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    private void Update()
    {
        Data = Severity switch
        {
            Severity.Critical => Icons.Critical,
            Severity.Error => Icons.Error,
            Severity.Warning => Icons.Warning,
            Severity.Info => Icons.Info,
            _ => Icons.Verbose,
        };
        Classes.Set("severity-critical", Severity == Severity.Critical);
        Classes.Set("severity-error", Severity == Severity.Error);
        Classes.Set("severity-warning", Severity == Severity.Warning);
        Classes.Set("severity-info", Severity == Severity.Info);
        Classes.Set("severity-verbose", Severity == Severity.Verbose);
        ToolTip.SetTip(this, SeverityMapper.DisplayName(Severity));
    }
}

/// <summary>Icon for how a boot ended.</summary>
public sealed class BootKindIcon : PathIcon
{
    public static readonly StyledProperty<ShutdownKind> KindProperty =
        AvaloniaProperty.Register<BootKindIcon, ShutdownKind>(nameof(Kind));

    static BootKindIcon()
    {
        KindProperty.Changed.AddClassHandler<BootKindIcon>((icon, _) => icon.Update());
    }

    public BootKindIcon()
    {
        Width = 18;
        Height = 18;
        Update();
    }

    protected override Type StyleKeyOverride => typeof(PathIcon);

    public ShutdownKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private void Update()
    {
        Data = Kind switch
        {
            ShutdownKind.Running => Icons.Play,
            ShutdownKind.PowerOff or ShutdownKind.Reboot => Icons.Check,
            ShutdownKind.Unexpected => Icons.Error,
            ShutdownKind.SuspendNeverResumed => Icons.Sleep,
            _ => Icons.Help,
        };
        Classes.Set("severity-info", Kind == ShutdownKind.Running);
        Classes.Set("boot-ok", Kind is ShutdownKind.PowerOff or ShutdownKind.Reboot);
        Classes.Set("severity-error", Kind == ShutdownKind.Unexpected);
        Classes.Set("severity-warning", Kind == ShutdownKind.SuspendNeverResumed);
        Classes.Set("severity-verbose", Kind == ShutdownKind.Unknown);
        ToolTip.SetTip(this, BootSession.DisplayName(Kind));
    }
}
