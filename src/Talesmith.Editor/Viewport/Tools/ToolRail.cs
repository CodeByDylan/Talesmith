using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Talesmith.Editor.Commands;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Path = Avalonia.Controls.Shapes.Path;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>The viewport's tool buttons, grouped and ordered as the <see cref="ToolManager"/> groups them.</summary>
/// <remarks>The buttons fill one column and continue in the next when the viewport is too short for them. When the viewport is too narrow for
/// another column as well, the last place holds a More button that lists the tools left over; it shows the active tool when that is one of
/// them, so the active tool is always in sight.</remarks>
public sealed class ToolRail : Panel
{
    public static readonly StyledProperty<ToolManager?> ManagerProperty = AvaloniaProperty.Register<ToolRail, ToolManager?>(nameof(Manager));

    private const double Spacing = 2;

    private static readonly Geometry Corner = Geometry.Parse("M 5,0 L 5,5 L 0,5 Z");

    private readonly List<Entry> _entries = [];
    private readonly Dictionary<Control, Rect> _places = new(ReferenceEqualityComparer.Instance);
    private readonly List<ToolItemViewModel> _overflow = [];
    private readonly ToggleButton _more;
    private readonly SymbolIcon _moreIcon = new() { Size = 17 };
    private readonly Path _moreCorner;
    private ToolManager? _subscribed;

    public ToolRail()
    {
        ClipToBounds = true;
        _moreCorner = new Path
        {
            Data = Corner,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -6, -6),
            IsVisible = false
        };
        _moreCorner.Bind(Shape.FillProperty, _moreCorner.GetResourceObservable("TextSecondaryBrush"));
        _more = new ToggleButton { Classes = { "tool" }, Content = new Panel { Children = { _moreIcon, _moreCorner } } };
        _more.Click += OnMoreClick;
        Children.Add(_more);
        UpdateMore();
    }

    public ToolManager? Manager
    {
        get => GetValue(ManagerProperty);
        set => SetValue(ManagerProperty, value);
    }

    /// <summary>The tools the rail has no room for, which the More button lists.</summary>
    public IReadOnlyList<ToolItemViewModel> Overflow => _overflow;

    /// <summary>The button of a tool, which may be one the rail has no room for.</summary>
    internal ToggleButton? ButtonOf(ToolItemViewModel tool) => _entries.Find(e => ReferenceEquals(e.Tool, tool))?.Control as ToggleButton;

    internal ToggleButton MoreButton => _more;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ManagerProperty)
            Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Manager);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            child.Measure(Size.Infinity);
        _places.Clear();
        _overflow.Clear();

        var columns = Flow(availableSize.Height);
        var room = double.IsInfinity(availableSize.Width)
            ? int.MaxValue
            : Math.Max(1, (int)((availableSize.Width + Spacing) / (ColumnWidth + Spacing)));
        if (columns.Count > room)
            Fold(columns, room, availableSize.Height);
        UpdateMore();
        _more.Measure(Size.Infinity);

        var width = 0.0;
        var height = 0.0;
        for (var c = 0; c < columns.Count; c++)
        {
            var x = c * (ColumnWidth + Spacing);
            var y = 0.0;
            foreach (var control in columns[c])
            {
                if (y > 0)
                    y += Spacing;
                var size = control.DesiredSize;
                _places[control] = new Rect(x + (ColumnWidth - size.Width) / 2, y, size.Width, size.Height);
                y += size.Height;
            }

            width = x + ColumnWidth;
            height = Math.Max(height, y);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Buttons without a place go left of the rail, where its clip hides them and keeps them from taking clicks.
        foreach (var child in Children)
            child.Arrange(_places.TryGetValue(child, out var place) ? place : new Rect(new Point(-child.DesiredSize.Width - Spacing, 0), child.DesiredSize));
        return finalSize;
    }

    private double ColumnWidth => Math.Max(_more.DesiredSize.Width, _entries.Count == 0 ? 0 : _entries.Max(e => e.Control.DesiredSize.Width));

    /// <summary>Places the buttons of the visible groups in columns no taller than <paramref name="height"/>, with a divider between groups
    /// that share a column.</summary>
    private List<List<Control>> Flow(double height)
    {
        var columns = new List<List<Control>> { new() };
        var used = 0.0;
        Control? divider = null;
        var started = false;
        foreach (var entry in _entries)
        {
            if (!entry.Group.IsVisible)
                continue;
            if (entry.Tool is null)
            {
                divider = started ? entry.Control : null;
                continue;
            }

            started = true;
            var column = columns[^1];
            var button = entry.Control.DesiredSize.Height;
            var needed = column.Count == 0 ? button : Spacing + button + (divider is null ? 0 : Spacing + divider.DesiredSize.Height);
            if (column.Count > 0 && used + needed > height)
            {
                columns.Add(column = []);
                used = 0;
                needed = button;
                divider = null;
            }

            if (divider is not null && column.Count > 0)
                column.Add(divider);
            column.Add(entry.Control);
            used += needed;
            divider = null;
        }

        if (columns[^1].Count == 0)
            columns.RemoveAt(columns.Count - 1);
        return columns;
    }

    /// <summary>Keeps the first <paramref name="room"/> columns and ends the last one with the More button, which lists the tools that no
    /// longer fit.</summary>
    private void Fold(List<List<Control>> columns, int room, double height)
    {
        foreach (var column in columns.Skip(room))
            _overflow.AddRange(column.Select(ToolOf).OfType<ToolItemViewModel>());
        columns.RemoveRange(room, columns.Count - room);

        var last = columns[^1];
        var more = _more.DesiredSize.Height;
        while (last.Count > 1 && ColumnHeight(last) + Spacing + more > height)
        {
            var removed = last[^1];
            last.RemoveAt(last.Count - 1);
            if (ToolOf(removed) is { } tool)
                _overflow.Insert(0, tool);
        }

        if (last.Count > 0 && ToolOf(last[^1]) is null)
            last.RemoveAt(last.Count - 1);
        if (last.Count == 1 && ColumnHeight(last) + Spacing + more > height && ToolOf(last[0]) is { } only)
        {
            _overflow.Insert(0, only);
            last.Clear();
        }

        last.Add(_more);
    }

    private static double ColumnHeight(List<Control> column) => column.Sum(c => c.DesiredSize.Height) + Spacing * Math.Max(0, column.Count - 1);

    private ToolItemViewModel? ToolOf(Control control) => _entries.Find(e => ReferenceEquals(e.Control, control))?.Tool;

    private void Rebuild()
    {
        Subscribe(null);
        foreach (var entry in _entries)
            Children.Remove(entry.Control);
        _entries.Clear();

        if (Manager is { } manager)
        {
            foreach (var group in manager.Groups)
            {
                _entries.Add(new Entry(group, null, new Border { Classes = { "divider-h" }, Margin = new Thickness(5, 3), Width = 22 }));
                foreach (var tool in group.Tools)
                    _entries.Add(new Entry(group, tool, CreateButton(tool)));
            }
        }

        Children.InsertRange(0, _entries.Select(e => e.Control));
        if (VisualRoot is not null)
            Subscribe(Manager);
        UpdateMore();
        InvalidateMeasure();
    }

    private static ToggleButton CreateButton(ToolItemViewModel tool)
    {
        var button = new ToggleButton
        {
            Classes = { "tool" },
            Content = new SymbolIcon { Data = tool.Icon, Size = 17 },
            IsChecked = tool.IsActive,
            IsEnabled = tool.IsAvailable
        };
        ToolTip.SetTip(button, new StackPanel
        {
            Spacing = 2,
            MaxWidth = 260,
            Children =
            {
                new TextBlock { Text = tool.ToolTip, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = tool.Description, TextWrapping = TextWrapping.Wrap, Classes = { "caption" } }
            }
        });
        button.Click += (_, _) =>
        {
            tool.IsActive = true;
            button.IsChecked = tool.IsActive;
        };
        return button;
    }

    /// <summary>Follows a tool manager's active tool and its tools' availability, or stops following when <paramref name="manager"/> is
    /// null.</summary>
    private void Subscribe(ToolManager? manager)
    {
        if (ReferenceEquals(manager, _subscribed))
            return;
        if (_subscribed is { } previous)
            Hook(previous, false);
        _subscribed = manager;
        if (manager is null)
            return;
        Hook(manager, true);
        SyncButtons();
    }

    private void Hook(ToolManager manager, bool hook)
    {
        if (hook)
            manager.PropertyChanged += OnManagerChanged;
        else
            manager.PropertyChanged -= OnManagerChanged;
        foreach (var group in manager.Groups)
        {
            if (hook)
                group.PropertyChanged += OnGroupChanged;
            else
                group.PropertyChanged -= OnGroupChanged;
            foreach (var tool in group.Tools)
            {
                if (hook)
                    tool.PropertyChanged += OnToolChanged;
                else
                    tool.PropertyChanged -= OnToolChanged;
            }
        }
    }

    private void OnManagerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolManager.ActiveTool))
            UpdateMore();
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolGroupViewModel.IsVisible))
            InvalidateMeasure();
    }

    private void OnToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ToolItemViewModel tool && ButtonOf(tool) is { } button)
            Sync(tool, button);
    }

    private void SyncButtons()
    {
        foreach (var entry in _entries)
        {
            if (entry is { Tool: { } tool, Control: ToggleButton button })
                Sync(tool, button);
        }

        UpdateMore();
    }

    private static void Sync(ToolItemViewModel tool, ToggleButton button)
    {
        button.IsChecked = tool.IsActive;
        button.IsEnabled = tool.IsAvailable;
    }

    /// <summary>Shows the active tool on the More button when it is one of the tools left over.</summary>
    private void UpdateMore()
    {
        var active = _overflow.Find(t => t.IsActive);
        _more.IsChecked = active is not null;
        _moreIcon.Data = active?.Icon ?? Icons.MoreHorizontal;
        _moreCorner.IsVisible = active is not null;
        ToolTip.SetTip(_more, active is null ? "More tools" : $"{active.ToolTip}, and more tools");
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e)
    {
        UpdateMore();
        CreateMoreMenu().ShowAt(_more);
    }

    /// <summary>A menu of the tools the rail has no room for, by group and with their shortcuts, that activates the one chosen.</summary>
    internal MenuFlyout CreateMoreMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.RightEdgeAlignedTop };
        string? group = null;
        foreach (var tool in _overflow)
        {
            if (group is not null && tool.Tool.Group != group)
                menu.Items.Add(new Separator());
            group = tool.Tool.Group;
            var item = new MenuItem
            {
                Header = tool.Tool.Name,
                Icon = new SymbolIcon { Data = tool.Icon, Size = 15 },
                InputGesture = tool.Tool.Shortcut is { } key ? KeyGestures.Parse(key) : null,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = tool.IsActive,
                IsEnabled = tool.IsAvailable
            };
            item.Click += (_, _) => tool.IsActive = true;
            menu.Items.Add(item);
        }

        return menu;
    }

    private sealed record Entry(ToolGroupViewModel Group, ToolItemViewModel? Tool, Control Control);
}
