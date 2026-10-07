using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Bobrlog.App.ViewModels;

namespace Bobrlog.App;

/// <summary>Maps XxxViewModel to Views.XxxView. All event list pages share one view type.</summary>
[RequiresUnreferencedCode("ViewLocator uses reflection")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);
        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is PageViewModel;
}
