using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Talesmith.Editor.Hierarchy;

/// <summary>The rows the hierarchy shows; inserts and removes runs of rows, such as a group being expanded, as one change.</summary>
public sealed class HierarchyRows : ObservableCollection<HierarchyRow>
{
    public HierarchyRows()
    {
    }

    public HierarchyRows(IEnumerable<HierarchyRow> rows)
        : base(rows)
    {
    }

    public void InsertRange(int index, IReadOnlyList<HierarchyRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)Count, nameof(index));
        if (rows.Count == 0)
            return;
        CheckReentrancy();
        ((List<HierarchyRow>)Items).InsertRange(index, rows);
        Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, rows.ToList(), index));
    }

    public void RemoveRange(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + count, Count, nameof(count));
        if (count == 0)
            return;
        CheckReentrancy();
        var removed = new List<HierarchyRow>(count);
        for (var i = 0; i < count; i++)
            removed.Add(Items[index + i]);
        ((List<HierarchyRow>)Items).RemoveRange(index, count);
        Changed(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index));
    }

    private void Changed(NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(e);
    }
}
