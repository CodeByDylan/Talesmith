using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>An item of a <see cref="PickerList"/>: a choice with an icon and a muted caption, or a group heading.</summary>
public sealed record PickerItem(string Title, Geometry? Icon = null, string? Caption = null, object? Value = null)
{
    /// <summary>Whether the item is a heading that groups the items below it and cannot be picked.</summary>
    public bool IsHeading { get; init; }

    /// <summary>A resource key of the icon's brush, such as "AccentBrush".</summary>
    public string? IconBrush { get; init; }
}

/// <summary>A search box over a list of choices, for pickers in flyouts: typing filters, arrows move, Enter picks the highlighted or first
/// choice and Escape closes.</summary>
public sealed class PickerList : UserControl
{
    private readonly SearchBox _search = new() { PlaceholderText = "Search", Margin = new Thickness(0, 0, 0, 6) };
    private readonly ListBox _list = new() { MaxHeight = 340, MinHeight = 40, Background = Brushes.Transparent, Padding = new Thickness(0) };
    private readonly TextBlock _empty = new() { Text = "Nothing matches", Classes = { "caption", "muted" }, Margin = new Thickness(8, 10), IsVisible = false };
    private readonly Func<string, IEnumerable<PickerItem>> _items;

    public PickerList(Func<string, IEnumerable<PickerItem>> items, string placeholder = "Search", double width = 300)
    {
        _items = items;
        _search.PlaceholderText = placeholder;
        Width = width;
        _list.ItemTemplate = new FuncDataTemplate<PickerItem>((item, _) => item is null ? null : Row(item));
        _list.AddHandler(PointerReleasedEvent, OnListReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _search.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                Refresh();
        };
        var panel = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        panel.Children.Add(_search);
        panel.Children.Add(new Panel { Children = { _list, _empty } });
        Content = panel;
    }

    /// <summary>Raised when an item was picked.</summary>
    public event EventHandler<PickerItem>? Picked;

    /// <summary>Clears the search, refills the list and focuses the search box.</summary>
    public void Open()
    {
        _search.Text = "";
        Refresh();
        Dispatcher.UIThread.Post(_search.FocusInput, DispatcherPriority.Input);
    }

    public void Refresh()
    {
        var items = _items(_search.Text?.Trim() ?? "").ToList();
        _list.ItemsSource = items;
        _empty.IsVisible = items.Count == 0;
        _list.SelectedItem = items.FirstOrDefault(i => !i.IsHeading);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_list.ItemsSource is not List<PickerItem> items)
            return;
        switch (e.Key)
        {
            case Key.Down or Key.Up:
            {
                var step = e.Key == Key.Down ? 1 : -1;
                var index = _list.SelectedIndex;
                do
                    index += step;
                while (index >= 0 && index < items.Count && items[index].IsHeading);
                if (index >= 0 && index < items.Count)
                {
                    _list.SelectedIndex = index;
                    _list.ScrollIntoView(index);
                }

                e.Handled = true;
                break;
            }
            case Key.Enter:
                if (_list.SelectedItem is PickerItem { IsHeading: false } selected)
                    Picked?.Invoke(this, selected);
                e.Handled = true;
                break;
        }
    }

    private void OnListReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && (e.Source as Control)?.DataContext is PickerItem { IsHeading: false } item)
            Picked?.Invoke(this, item);
    }

    private static Control Row(PickerItem item)
    {
        if (item.IsHeading)
        {
            return new TextBlock
            {
                Text = item.Title.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                LetterSpacing = 0.6,
                Margin = new Thickness(2, 8, 0, 2),
                Classes = { "muted" },
                IsHitTestVisible = false
            };
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = item.Caption is null ? 24 : 34, Background = Brushes.Transparent };
        if (item.Icon is not null)
        {
            var icon = new SymbolIcon { Data = item.Icon, Size = 15, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(icon.With(SymbolIcon.ForegroundProperty, item.IconBrush ?? "TextSecondaryBrush"));
        }

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(new TextBlock { Text = item.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Caption is { } caption)
            text.Children.Add(new TextBlock { Text = caption, Classes = { "caption", "muted" }, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    /// <summary>Opens a picker in a flyout under <paramref name="anchor"/> and runs <paramref name="picked"/> with the chosen item.</summary>
    public static Flyout Attach(Control anchor, Func<string, IEnumerable<PickerItem>> items, Action<PickerItem> picked, string placeholder = "Search", double width = 300)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        var picker = new PickerList(items, placeholder, width);
        var flyout = new Flyout { Content = picker, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Opened += (_, _) => picker.Open();
        picker.Picked += (_, item) =>
        {
            flyout.Hide();
            picked(item);
        };
        return flyout;
    }
}
