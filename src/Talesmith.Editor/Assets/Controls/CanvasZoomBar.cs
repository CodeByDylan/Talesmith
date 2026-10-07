using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Assets.Controls;

/// <summary>Floating zoom buttons for a <see cref="PixelCanvas"/>: zoom out, the zoom level (click for 100%), zoom in and fit.</summary>
public sealed class CanvasZoomBar : Border
{
    public static readonly StyledProperty<PixelCanvas?> TargetProperty = AvaloniaProperty.Register<CanvasZoomBar, PixelCanvas?>(nameof(Target));

    private readonly TextBlock _text = new() { Classes = { "mono" }, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public CanvasZoomBar()
    {
        Classes.Add("floating");
        Padding = new Thickness(2);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(10);
        var actual = new Button { Classes = { "subtle", "small" }, MinWidth = 50, Content = _text, Focusable = false, Padding = new Thickness(4, 0) };
        ToolTip.SetTip(actual, "Actual size");
        actual.Click += (_, _) => Target?.ZoomToActual();
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            Children =
            {
                IconButton(Icons.Minus, "Zoom out", () => Target?.ZoomBy(0.8)),
                actual,
                IconButton(Icons.Plus, "Zoom in", () => Target?.ZoomBy(1.25)),
                IconButton(Icons.Maximize, "Fit (double-click the image)", () => Target?.ZoomToFit())
            }
        };
    }

    public PixelCanvas? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TargetProperty)
            return;
        if (change.OldValue is PixelCanvas old)
            old.PropertyChanged -= OnTargetChanged;
        if (change.NewValue is PixelCanvas canvas)
        {
            canvas.PropertyChanged += OnTargetChanged;
            Show(canvas.Zoom);
        }
    }

    private void OnTargetChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == PixelCanvas.ZoomProperty)
            Show((double)e.NewValue!);
    }

    private void Show(double zoom) => _text.Text = (zoom * 100).ToString(zoom < 0.1 ? "0.#" : "0", CultureInfo.InvariantCulture) + "%";

    private static Button IconButton(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 13 } };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }
}
