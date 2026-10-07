using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.Avalonia.Presentation;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Overlays;

/// <summary>Holds the <see cref="IGameOverlay"/> controls on the game's view: placed on its view rectangle and scaled so that their layout
/// size is the view size in overlay units.</summary>
/// <remarks>
/// <para>Overlays lay out in overlay units, so a HUD keeps its place and size relative to the game at any window size. Without an overlay
/// size in <see cref="ViewSettings"/> an overlay unit is a view unit, the unit of <see cref="RenderSpace.Screen"/> draws; with one, overlays
/// lay out in the view size times <see cref="ViewSettings.OverlayUnitsPerViewUnit"/>, which is the overlay size in
/// <see cref="ViewScaleMode.Fit"/>, so UI designed at 1280×720 keeps its size on a 640×360 pixel-art view.</para>
/// <para>The scale is a render transform: Avalonia draws text and vector content at the final size, so it stays sharp, and pointer input is
/// transformed back, so buttons work at any scale. Content outside the view is clipped, leaving the bars empty.</para>
/// <para>Overlays can read <see cref="OverlaySize"/> and <see cref="OverlayScale"/> from the nearest <see cref="GameOverlayLayer"/> ancestor
/// to adapt their layout.</para>
/// </remarks>
public sealed class GameOverlayLayer : Decorator
{
    public static readonly DirectProperty<GameOverlayLayer, Size> OverlaySizeProperty =
        AvaloniaProperty.RegisterDirect<GameOverlayLayer, Size>(nameof(OverlaySize), layer => layer.OverlaySize);

    public static readonly DirectProperty<GameOverlayLayer, double> OverlayScaleProperty =
        AvaloniaProperty.RegisterDirect<GameOverlayLayer, double>(nameof(OverlayScale), layer => layer.OverlayScale);

    private readonly Viewport _viewport;
    private readonly Panel _overlays = new() { ClipToBounds = true, RenderTransformOrigin = RelativePoint.TopLeft };
    private Size _overlaySize;
    private double _overlayScale = 1;

    public GameOverlayLayer(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        _viewport = viewport;
        Child = _overlays;
    }

    /// <summary>The overlay controls, stacked in order.</summary>
    public Controls Overlays => _overlays.Children;

    /// <summary>The size overlays lay out in, in overlay units.</summary>
    public Size OverlaySize
    {
        get => _overlaySize;
        private set => SetAndRaise(OverlaySizeProperty, ref _overlaySize, value);
    }

    /// <summary>Logical pixels per overlay unit: how much the overlays are scaled.</summary>
    public double OverlayScale
    {
        get => _overlayScale;
        private set => SetAndRaise(OverlayScaleProperty, ref _overlayScale, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GameView.DisplayScaleProperty)
            InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : 0, double.IsFinite(availableSize.Height) ? availableSize.Height : 0);
        _overlays.Measure(Place(size).OverlaySize);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (position, overlaySize, scale) = Place(finalSize);
        OverlaySize = overlaySize;
        if (scale != OverlayScale || _overlays.RenderTransform is null)
        {
            OverlayScale = scale;
            _overlays.RenderTransform = new ScaleTransform(scale, scale);
        }

        _overlays.Arrange(new Rect(position, overlaySize));
        return finalSize;
    }

    /// <summary>Lays the view out on a size the way the game view does: where the overlays go and how large and how scaled they are, in
    /// logical pixels.</summary>
    private (Point Position, Size OverlaySize, double Scale) Place(Size size)
    {
        var displayScale = GameView.DisplayScaleOf(this);
        var pixels = PixelSize.FromSize(size, displayScale);
        var view = _viewport.View;
        var layout = ViewLayout.Compute(new System.Numerics.Vector2(pixels.Width, pixels.Height), (float)displayScale, view);
        double units = view.OverlayUnitsPerViewUnit();
        return (new Point(layout.ViewRect.X / displayScale, layout.ViewRect.Y / displayScale),
            new Size(layout.ViewSize.X * units, layout.ViewSize.Y * units), layout.Scale / displayScale / units);
    }
}
