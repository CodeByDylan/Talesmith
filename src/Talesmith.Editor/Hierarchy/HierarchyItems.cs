using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;

namespace Talesmith.Editor.Hierarchy;

/// <summary>The hierarchy's rows; each container is a <see cref="HierarchyRowView"/> that recycling hands a new row, so scrolling never rebuilds rows.</summary>
public sealed class HierarchyItems : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new HierarchyRowView();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = DefaultRecycleKey;
        return item is not HierarchyRowView;
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index) => container.DataContext = item;

    protected override void ClearContainerForItemOverride(Control container)
    {
    }
}

/// <summary>A hierarchy row: a <c>Border.hrow</c> marked <c>selected</c> while its row is, around a <see cref="HierarchyRowContent"/>.</summary>
public sealed class HierarchyRowView : Border
{
    private HierarchyRow? _row;

    public HierarchyRowView()
    {
        Classes.Add("hrow");
        var label = new Panel();
        label.Classes.Add("label");
        label.Children.Add(new HierarchyRowContent());
        Child = label;
    }

    protected override Type StyleKeyOverride => typeof(Border);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Track(DataContext as HierarchyRow);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Track(DataContext as HierarchyRow);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Track(null);
    }

    private void Track(HierarchyRow? row)
    {
        if (_row is not null)
            _row.PropertyChanged -= OnRowChanged;
        _row = row;
        if (row is not null && VisualRoot is not null)
            row.PropertyChanged += OnRowChanged;
        Classes.Set("selected", row?.IsSelected == true);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HierarchyRow.IsSelected) or null)
            Classes.Set("selected", _row?.IsSelected == true);
    }
}
