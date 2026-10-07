using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Talesmith.Avalonia.Presentation;

namespace Talesmith.Editor.PlayMode;

/// <summary>Shows its child as a game window of the <see cref="Window"/> size and display scale, at a <see cref="Zoom"/> or as large as fits up
/// to its actual size, inside a ring; without a window, the child fills the frame inside the ring, as a window the size of the frame.</summary>
/// <remarks>The child lays out at the window's size in logical pixels of its display, and <see cref="GameView.DisplayScaleProperty"/> gives a
/// game in it that display's scale, so the game renders the window's pixels and lays out its view and overlays for that display. A render
/// transform then shows the child at the zoom, and maps pointer input back into the window.</remarks>
public sealed class GameWindowFrame : Decorator
{
    public static readonly StyledProperty<GameWindowSize?> WindowProperty = AvaloniaProperty.Register<GameWindowFrame, GameWindowSize?>(nameof(Window));

    public static readonly StyledProperty<double?> ZoomProperty =
        AvaloniaProperty.Register<GameWindowFrame, double?>(nameof(Zoom), validate: zoom => zoom is null || (double.IsFinite(zoom.Value) && zoom > 0));

    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<GameWindowFrame, IBrush?>(nameof(RingBrush));

    public static readonly DirectProperty<GameWindowFrame, double> ActualZoomProperty =
        AvaloniaProperty.RegisterDirect<GameWindowFrame, double>(nameof(ActualZoom), frame => frame.ActualZoom);

    private const double Ring = 2;
    private const double Gap = 16;

    private TopLevel? _topLevel;
    private Rect _window;
    private double _actualZoom = 1;

    static GameWindowFrame()
    {
        AffectsMeasure<GameWindowFrame>(WindowProperty, ZoomProperty);
        AffectsRender<GameWindowFrame>(RingBrushProperty);
    }

    public GameWindowFrame() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    /// <summary>The window to show the child as, or null to fill the frame.</summary>
    public GameWindowSize? Window
    {
        get => GetValue(WindowProperty);
        set => SetValue(WindowProperty, value);
    }

    /// <summary>Screen pixels per window pixel, or null to show the window as large as fits, up to its actual size.</summary>
    public double? Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public IBrush? RingBrush
    {
        get => GetValue(RingBrushProperty);
        set => SetValue(RingBrushProperty, value);
    }

    /// <summary>The zoom the window shows at: <see cref="Zoom"/>, or the one that fits; 1 without a window.</summary>
    public double ActualZoom
    {
        get => _actualZoom;
        private set => SetAndRaise(ActualZoomProperty, ref _actualZoom, value);
    }

    /// <summary>The window's area in the frame, inside the ring.</summary>
    public Rect WindowBounds => _window;

    public override void Render(DrawingContext context)
    {
        if (_window.Width <= 0 || _window.Height <= 0)
            return;
        var ring = _window.Inflate(Ring);
        if (Window is not null && this.TryFindResource("ShadowRaised", ActualThemeVariant, out var shadow) && shadow is BoxShadows shadows)
            context.DrawRectangle(Brushes.Transparent, null, new RoundedRect(ring), shadows);
        if (RingBrush is { } brush)
            context.DrawRectangle(null, new Pen(brush, Ring), _window.Inflate(Ring / 2));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowProperty)
            GameView.SetDisplayScale(this, Window?.DisplayScale);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
            _topLevel.ScalingChanged += OnScalingChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
            _topLevel.ScalingChanged -= OnScalingChanged;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = Layout(availableSize);
        Child?.Measure(layout.ContentSize);
        return layout.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = Layout(finalSize);
        ActualZoom = layout.Zoom;
        if (layout.Window != _window)
        {
            _window = layout.Window;
            InvalidateVisual();
        }

        if (Child is { } child)
        {
            Show(child, layout.ContentScale, layout.Zoom);
            child.Arrange(new Rect(layout.Window.Position, layout.ContentSize));
        }

        return finalSize;
    }

    private GameWindowLayout Layout(Size room) => GameWindowLayout.Compute(room, _topLevel?.RenderScaling ?? 1, Window, Zoom, Ring, Gap);

    /// <summary>Scales the child to the zoom, sampling its frames by the nearest pixel when they are magnified by a whole number, to keep pixels
    /// sharp.</summary>
    private static void Show(Control child, double scale, double zoom)
    {
        child.ClipToBounds = true;
        child.RenderTransformOrigin = RelativePoint.TopLeft;
        if (scale == 1)
            child.RenderTransform = null;
        else if (child.RenderTransform is not ScaleTransform transform || transform.ScaleX != scale)
            child.RenderTransform = new ScaleTransform(scale, scale);
        RenderOptions.SetBitmapInterpolationMode(child, zoom >= 2 && zoom == Math.Floor(zoom) ? BitmapInterpolationMode.None : BitmapInterpolationMode.Unspecified);
    }

    private void OnScalingChanged(object? sender, EventArgs e) => InvalidateMeasure();
}
