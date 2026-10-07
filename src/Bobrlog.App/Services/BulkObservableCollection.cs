using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Bobrlog.App.Services;

/// <summary>ObservableCollection with a single Reset notification for bulk changes.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
            Items.Add(item);
        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
