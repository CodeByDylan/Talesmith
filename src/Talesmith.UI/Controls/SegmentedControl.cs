using Avalonia.Controls;

namespace Talesmith.UI.Controls;

/// <summary>A compact single-selection group rendered as a row of joined segments.</summary>
public class SegmentedControl : ListBox
{
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) =>
        new SegmentedControlItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey) =>
        NeedsContainer<SegmentedControlItem>(item, out recycleKey);
}

/// <summary>A segment within a <see cref="SegmentedControl"/>.</summary>
public class SegmentedControlItem : ListBoxItem;
