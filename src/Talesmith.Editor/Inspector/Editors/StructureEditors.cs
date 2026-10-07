using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>A labelled, collapsible block of nested rows for objects and lists, spanning the row's width.</summary>
public sealed class Foldout : StackPanel, IFullWidthEditor
{
    private readonly Button _chevron;
    private readonly SymbolIcon _chevronIcon = new() { Data = Icons.ChevronRight, Size = 11, StrokeThickness = 2.5 };
    private readonly Border _body;
    private bool _isExpanded;

    public Foldout(string label, string? tooltip)
    {
        Spacing = 2;
        _chevronIcon.With(SymbolIcon.ForegroundProperty, "TextMutedBrush");
        _chevronIcon.RenderTransformOrigin = RelativePoint.Center;
        Label = new TextBlock { Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        _chevron = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(0, 0, 6, 0),
            MinHeight = 26,
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { _chevronIcon, Label }
            }
        };
        Label.With(TextBlock.ForegroundProperty, "TextSecondaryBrush");
        ToolTip.SetTip(Label, tooltip);
        _chevron.Click += (_, _) => IsExpanded = !IsExpanded;
        Header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { _chevron } };
        HeaderActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(HeaderActions, 1);
        Header.Children.Add(HeaderActions);
        _body = new Border { Margin = new Thickness(5, 0, 0, 4), Padding = new Thickness(10, 0, 0, 0), BorderThickness = new Thickness(1, 0, 0, 0), IsVisible = false };
        _body.With(Border.BorderBrushProperty, "BorderSubtleBrush");
        Children.Add(Header);
        Children.Add(_body);
    }

    public Grid Header { get; }

    public TextBlock Label { get; }

    /// <summary>Controls at the right of the header, such as a count or an add button.</summary>
    public StackPanel HeaderActions { get; }

    /// <summary>Called the first time the foldout opens and whenever <see cref="Invalidate"/> asks for new content while it is open.</summary>
    public Func<Control?>? BuildContent { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            _chevronIcon.RenderTransform = value ? new RotateTransform(90) : null;
            if (value && _body.Child is null)
                _body.Child = BuildContent?.Invoke();
            _body.IsVisible = value && _body.Child is not null;
        }
    }

    /// <summary>Drops the content so it is built again, now when open or else when next opened.</summary>
    public void Invalidate()
    {
        _body.Child = null;
        if (_isExpanded)
        {
            _body.Child = BuildContent?.Invoke();
            _body.IsVisible = _body.Child is not null;
        }
    }
}

/// <summary>Objects as a foldout of their fields; nullable objects get a checkbox that creates or clears them.</summary>
public sealed class ObjectEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Property.Kind != PropertyKind.Object || context.Value is not PropertyValue value)
            return null;
        var property = context.Property;
        var factory = context.Services.GetRequiredService<PropertyEditorFactory>();
        var foldout = new Foldout(property.Label, property.Tooltip);
        var owned = new List<PropertyValue>();
        foldout.BuildContent = () =>
        {
            Release(owned);
            if (value.Get() is not JsonObject)
                return null;
            using (value.Data.Collect(owned))
                return factory.CreateRows(property.Children, child => value.Child(child.Name, child));
        };

        CheckBox? toggle = null;
        var updating = false;
        if (property.IsNullable)
        {
            toggle = new CheckBox { MinHeight = 0, Padding = default };
            ToolTip.SetTip(toggle, "Set a value; clear to leave it unset");
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (updating)
                    return;
                value.Set(toggle.IsChecked == true ? JsonValues.DefaultFor(property with { IsNullable = false }) : null);
            };
            foldout.HeaderActions.Children.Add(toggle);
        }

        var wasSet = value.Get() is JsonObject;
        return PropertyEditors.Watch(foldout, value, () =>
        {
            var isSet = value.Get() is JsonObject;
            if (toggle is not null)
            {
                updating = true;
                toggle.IsChecked = value.IsMixed ? null : isSet;
                updating = false;
            }

            foldout.Label.Opacity = isSet ? 1 : 0.55;
            if (isSet != wasSet)
            {
                wasSet = isSet;
                foldout.Invalidate();
                if (isSet && !foldout.IsExpanded)
                    foldout.IsExpanded = true;
            }
        });
    }

    internal static void Release(List<PropertyValue> values)
    {
        foreach (var value in values)
            value.Dispose();
        values.Clear();
    }
}

/// <summary>Lists as a foldout with a count and an add button; items can be dragged to reorder, duplicated and removed, each change one undo step.</summary>
public sealed class ListEditorProvider : IPropertyEditorProvider
{
    private static readonly DataFormat<ListItemDrag> DragFormat = DataFormat.CreateInProcessFormat<ListItemDrag>("talesmith.inspector-list-item");

    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Property.Kind != PropertyKind.List || context.Value is not PropertyValue value)
            return null;
        var property = context.Property;
        var element = property.Element ?? new PropertyDescriptor("item", "Item", PropertyKind.String, typeof(string));
        var factory = context.Services.GetRequiredService<PropertyEditorFactory>();
        var foldout = new Foldout(property.Label, property.Tooltip);
        var count = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0), MinWidth = 20, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        var countText = new TextBlock { FontSize = 10, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        count.Child = countText;
        count.With(Border.BackgroundProperty, "SurfaceSunkenBrush");
        var add = new Button { Classes = { "icon", "small" }, Width = 22, Height = 22, Focusable = false, Content = new SymbolIcon { Data = Icons.Plus, Size = 13 } };
        ToolTip.SetTip(add, "Add an item");
        foldout.HeaderActions.Children.Add(count);
        foldout.HeaderActions.Children.Add(add);

        var owned = new List<PropertyValue>();
        var shownCount = -1;
        JsonArray Current() => value.Get() as JsonArray ?? [];

        void Change(Action<JsonArray> change)
        {
            var array = (JsonArray)Current().DeepClone();
            change(array);
            value.Set(array);
        }

        add.Click += (_, _) =>
        {
            Change(array => array.Add(array.Count > 0 ? array[^1]?.DeepClone() : JsonValues.DefaultFor(element)));
            foldout.IsExpanded = true;
        };

        foldout.BuildContent = () =>
        {
            ObjectEditorProvider.Release(owned);
            var items = Current();
            var stack = new StackPanel { Spacing = 2 };
            using (value.Data.Collect(owned))
            {
                for (var i = 0; i < items.Count; i++)
                    stack.Children.Add(Item(factory, value, element, i, Change));
            }

            if (items.Count == 0)
                stack.Children.Add(new TextBlock { Text = "Empty", Classes = { "caption", "muted" }, Margin = new Thickness(2, 4) });
            return stack;
        };

        return PropertyEditors.Watch(foldout, value, () =>
        {
            var items = Current();
            countText.Text = value.IsMixed ? "—" : items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            add.IsEnabled = !value.IsReadOnly;
            if (items.Count != shownCount)
            {
                shownCount = items.Count;
                foldout.Invalidate();
            }
        });
    }

    private static Grid Item(PropertyEditorFactory factory, PropertyValue list, PropertyDescriptor element, int index, Action<Action<JsonArray>> change)
    {
        var itemValue = list.Child(index.ToString(System.Globalization.CultureInfo.InvariantCulture), element with { Label = $"Element {index}" });
        var editor = factory.CreateRow(element with { Label = $"Element {index}", Header = null }, itemValue);
        if (editor is PropertyRow row)
        {
            row.Label = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row.LabelWidth = 26;
        }

        var grip = new Border { Width = 14, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), VerticalAlignment = VerticalAlignment.Stretch };
        grip.Child = new SymbolIcon { Data = Icons.GripVertical, Size = 12, VerticalAlignment = editor is IFullWidthEditor ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, 7, 0, 0) }
            .With(SymbolIcon.ForegroundProperty, "TextMutedBrush");
        ToolTip.SetTip(grip, "Drag to reorder");

        var more = new Button
        {
            Classes = { "icon", "small" },
            Width = 20,
            Height = 20,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 3, 0, 0),
            Content = new SymbolIcon { Data = Icons.MoreHorizontal, Size = 12 }
        };
        more.Flyout = new MenuFlyout
        {
            ItemsSource = new Control[]
            {
                MenuItem("Duplicate", Icons.CopyPlus, () => change(array => array.Insert(index + 1, array[index]?.DeepClone()))),
                MenuItem("Move up", Icons.ArrowUp, () => change(array => Move(array, index, index - 1))),
                MenuItem("Move down", Icons.ArrowDown, () => change(array => Move(array, index, index + 1))),
                new Separator(),
                MenuItem("Remove", Icons.Trash, () => change(array => array.RemoveAt(index)))
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Background = Brushes.Transparent };
        Grid.SetColumn(editor, 1);
        Grid.SetColumn(more, 2);
        grid.Children.Add(grip);
        grid.Children.Add(editor);
        grid.Children.Add(more);

        grip.PointerPressed += async (_, e) =>
        {
            if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed || list.IsReadOnly)
                return;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(DragFormat, new ListItemDrag(list.Data, list.Component, list.Path, index)));
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        };
        DragDrop.SetAllowDrop(grid, true);
        DragDrop.AddDragOverHandler(grid, (_, e) =>
        {
            if (e.DataTransfer.TryGetValue(DragFormat) is not { } drag || !drag.IsSameList(list))
                return;
            DropIndicator.Show(grid, DropIndicator.PositionAt(grid, e.GetPosition(grid), canDropInside: false));
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        });
        DragDrop.AddDragLeaveHandler(grid, (_, _) => DropIndicator.Hide(grid));
        DragDrop.AddDropHandler(grid, (_, e) =>
        {
            DropIndicator.Hide(grid);
            if (e.DataTransfer.TryGetValue(DragFormat) is not { } drag || !drag.IsSameList(list))
                return;
            var after = DropIndicator.PositionAt(grid, e.GetPosition(grid), canDropInside: false) == DropPosition.After;
            var target = index + (after ? 1 : 0);
            if (drag.Index < target)
                target--;
            if (target != drag.Index)
                change(array => Move(array, drag.Index, target));
            e.Handled = true;
        });
        return grid;
    }

    private static void Move(JsonArray array, int from, int to)
    {
        if (from < 0 || from >= array.Count || to < 0 || to >= array.Count || from == to)
            return;
        var item = array[from];
        array.RemoveAt(from);
        array.Insert(to, item);
    }

    private static MenuItem MenuItem(string header, Geometry icon, Action action)
    {
        var item = new MenuItem { Header = header, Icon = new SymbolIcon { Data = icon, Size = 14 } };
        item.Click += (_, _) => action();
        return item;
    }

    private sealed record ListItemDrag(InspectorData Data, string Component, string Path, int Index)
    {
        public bool IsSameList(PropertyValue list) => ReferenceEquals(Data, list.Data) && Component == list.Component && Path == list.Path;
    }
}
