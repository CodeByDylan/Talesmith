using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>The frame of reference fields such as asset and entity fields: a field-styled box with a clickable icon and name, and small action
/// buttons at the right.</summary>
internal sealed class FieldBox : Border
{
    private readonly SymbolIcon _icon = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _text = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 0, Margin = new Thickness(0, 0, 2, 0) };
    private string _iconBrush = "TextSecondaryBrush";

    public FieldBox()
    {
        Classes.Add("field-box");
        MinHeight = 26;
        CornerRadius = new CornerRadius(6);
        BorderThickness = new Thickness(1);
        this.With(BackgroundProperty, "TextControlBackground").With(BorderBrushProperty, "TextControlBorderBrush");
        Main = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(8, 0, 4, 0),
            MinHeight = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new DockPanel { Children = { _icon, _text } }
        };
        DockPanel.SetDock(_icon, Dock.Left);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_actions, 1);
        grid.Children.Add(Main);
        grid.Children.Add(_actions);
        Child = grid;
        _icon.With(SymbolIcon.ForegroundProperty, _iconBrush);
    }

    /// <summary>The clickable part with the icon and name, which opens the picker.</summary>
    public Button Main { get; }

    /// <summary>Shows a value: its icon, text and whether it is a placeholder (muted) or a problem (red).</summary>
    public void Show(Geometry? icon, string text, string iconBrush, bool muted, bool error = false)
    {
        _icon.Data = icon;
        _icon.IsVisible = icon is not null;
        _text.Text = text;
        if (iconBrush != _iconBrush)
        {
            _iconBrush = iconBrush;
            _icon.With(SymbolIcon.ForegroundProperty, iconBrush);
        }

        _text.With(TextBlock.ForegroundProperty, error ? "DangerBrush" : muted ? "TextMutedBrush" : "TextPrimaryBrush");
        _text.FontStyle = muted ? FontStyle.Italic : FontStyle.Normal;
    }

    /// <summary>Adds a small icon button at the right.</summary>
    public Button AddAction(Geometry icon, string tip, Action click)
    {
        var button = new Button { Classes = { "icon", "small" }, Width = 22, Height = 22, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 12 } };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        _actions.Children.Add(button);
        return button;
    }

    /// <summary>Shows the drop highlight while something droppable is dragged over the field.</summary>
    public void SetDropTarget(bool active)
    {
        if (active)
            this.With(BorderBrushProperty, "AccentBrush");
        else
            this.With(BorderBrushProperty, "TextControlBorderBrush");
    }
}
