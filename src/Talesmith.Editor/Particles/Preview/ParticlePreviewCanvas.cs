using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using Talesmith.Avalonia.Presentation;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Color = Talesmith.Mathematics.Color;
using UiColor = global::Avalonia.Media.Color;

namespace Talesmith.Editor.Particles.Preview;

/// <summary>What the preview draws behind the particles.</summary>
public enum PreviewBackground
{
    Dark,
    Light,

    /// <summary>A checkerboard, to judge transparency.</summary>
    Checker,

    /// <summary>The open scene's background color.</summary>
    Scene
}

/// <summary>Shows a <see cref="ParticlePreviewPlayer"/> at the display's frame rate while visible, drawn by the preview's own Skia renderer.</summary>
/// <remarks>
/// The simulation advances and a frame is built on the UI thread; Avalonia's render thread draws the newest frame onto its own canvas, so
/// previews never wait for each other or the scene. Overlays (grid, shape, bounds) are drawn above with Avalonia. With
/// <see cref="IsInteractive"/>, the wheel zooms around the pointer, dragging pans and a double-click returns to automatic framing.
/// </remarks>
public sealed class ParticlePreviewCanvas : Control
{
    public static readonly StyledProperty<ParticlePreviewPlayer?> PlayerProperty =
        AvaloniaProperty.Register<ParticlePreviewCanvas, ParticlePreviewPlayer?>(nameof(Player));

    public static readonly StyledProperty<ParticlePreviewRenderer?> PreviewRendererProperty =
        AvaloniaProperty.Register<ParticlePreviewCanvas, ParticlePreviewRenderer?>(nameof(PreviewRenderer));

    public static readonly StyledProperty<PreviewBackground> BackgroundModeProperty =
        AvaloniaProperty.Register<ParticlePreviewCanvas, PreviewBackground>(nameof(BackgroundMode));

    public static readonly StyledProperty<Color> SceneColorProperty =
        AvaloniaProperty.Register<ParticlePreviewCanvas, Color>(nameof(SceneColor), new Color(27, 58, 92));

    public static readonly StyledProperty<bool> ShowGridProperty = AvaloniaProperty.Register<ParticlePreviewCanvas, bool>(nameof(ShowGrid));

    public static readonly StyledProperty<bool> ShowShapeProperty = AvaloniaProperty.Register<ParticlePreviewCanvas, bool>(nameof(ShowShape));

    public static readonly StyledProperty<bool> ShowBoundsProperty = AvaloniaProperty.Register<ParticlePreviewCanvas, bool>(nameof(ShowBounds));

    public static readonly StyledProperty<bool> IsInteractiveProperty = AvaloniaProperty.Register<ParticlePreviewCanvas, bool>(nameof(IsInteractive));

    /// <summary>The largest frame rate; thumbnails use less.</summary>
    public static readonly StyledProperty<double> MaxFramesPerSecondProperty =
        AvaloniaProperty.Register<ParticlePreviewCanvas, double>(nameof(MaxFramesPerSecond), 0);

    private static readonly Color DarkBackground = new(20, 22, 28);
    private static readonly Color LightBackground = new(232, 235, 240);
    private static readonly Color CheckerLight = new(206, 209, 216);
    private static readonly Color CheckerDark = new(160, 165, 175);
    private static readonly IPen ShapePen = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(230, 120, 200, 255)), 1.5);
    private static readonly IPen BoundsPen = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(200, 255, 196, 64)), 1,
        new ImmutableDashStyle([4, 3], 0));
    private static readonly IPen OriginPen = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(220, 255, 255, 255)), 1.25);

    private static readonly IPen GridMinorDark = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(22, 255, 255, 255)), 1);
    private static readonly IPen GridAxisDark = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(56, 255, 255, 255)), 1);
    private static readonly IPen GridMinorLight = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(26, 0, 0, 0)), 1);
    private static readonly IPen GridAxisLight = new ImmutablePen(new ImmutableSolidColorBrush(UiColor.FromArgb(60, 0, 0, 0)), 1);

    private readonly FrameExchange _frames = new();
    private readonly Action<TimeSpan> _onAnimationFrame;
    private readonly DispatcherTimer _visibilityPoll;
    private SpriteInstance[] _instances = new SpriteInstance[256];
    private Vector2 _center;
    private float _zoom = 1;
    private bool _autoFrame = true;
    private bool _snapFraming = true;

    /// <summary>Everything a thumbnail has shown so far, so it frames a whole one-shot effect instead of following it.</summary>
    private Rect2 _framedArea;
    private bool _animating;
    private bool _framePending;
    private long _lastFrame;
    private Point? _dragStart;
    private Vector2 _dragCenter;
    private StreamGeometry? _shapeGeometry;
    private (VFX.ShapeModule? Shape, VFX.ParticleShapeKind Kind, Vector2 Center, float Zoom, Size Size, int Version) _shapeKey;
    private int _settingsVersion;
    private VFX.ParticleSettings? _lastSettings;

    public ParticlePreviewCanvas()
    {
        ClipToBounds = true;
        Focusable = false;
        _onAnimationFrame = OnAnimationFrame;
        _visibilityPoll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _visibilityPoll.Tick += (_, _) => StartIfVisible();
    }

    public ParticlePreviewPlayer? Player
    {
        get => GetValue(PlayerProperty);
        set => SetValue(PlayerProperty, value);
    }

    public ParticlePreviewRenderer? PreviewRenderer
    {
        get => GetValue(PreviewRendererProperty);
        set => SetValue(PreviewRendererProperty, value);
    }

    public PreviewBackground BackgroundMode
    {
        get => GetValue(BackgroundModeProperty);
        set => SetValue(BackgroundModeProperty, value);
    }

    public Color SceneColor
    {
        get => GetValue(SceneColorProperty);
        set => SetValue(SceneColorProperty, value);
    }

    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool ShowShape
    {
        get => GetValue(ShowShapeProperty);
        set => SetValue(ShowShapeProperty, value);
    }

    public bool ShowBounds
    {
        get => GetValue(ShowBoundsProperty);
        set => SetValue(ShowBoundsProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public double MaxFramesPerSecond
    {
        get => GetValue(MaxFramesPerSecondProperty);
        set => SetValue(MaxFramesPerSecondProperty, value);
    }

    /// <summary>The zoom in screen pixels per world unit.</summary>
    public float Zoom => _zoom;

    /// <summary>Whether the view follows the effect; panning or zooming turns it off.</summary>
    public bool IsAutoFraming => _autoFrame;

    /// <summary>Follows the effect again.</summary>
    public void FrameEffect()
    {
        _autoFrame = true;
        _snapFraming = true;
    }

    /// <summary>Sets the zoom around the view's center and stops following the effect.</summary>
    public void SetZoom(float zoom)
    {
        _autoFrame = false;
        _zoom = Math.Clamp(zoom, 0.05f, 16f);
    }

    /// <summary>Builds and publishes a frame now, for tests and captures without a running animation loop.</summary>
    public void RenderNow()
    {
        BuildFrame();
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 1 || bounds.Height < 1)
            return;
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var pixels = new PixelSize(Math.Max(1, (int)Math.Round(bounds.Width * scaling)), Math.Max(1, (int)Math.Round(bounds.Height * scaling)));
        if (PreviewRenderer is { IsDisposed: false } renderer)
            context.Custom(new DrawOperation(this, renderer, bounds, pixels));
        DrawOverlays(context, bounds);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _visibilityPoll.Start();
        StartIfVisible();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _visibilityPoll.Stop();
        _animating = false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty)
        {
            _autoFrame = true;
            _snapFraming = true;
            _framedArea = default;
            _lastSettings = null;
        }

        if (change.Property == IsVisibleProperty)
            StartIfVisible();
        if (change.Property != BoundsProperty)
            InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsInteractive)
            return;
        var position = e.GetPosition(this);
        var before = ScreenToWorld(position);
        _autoFrame = false;
        _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, (float)e.Delta.Y), 0.05f, 16f);
        _center += before - ScreenToWorld(position);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsInteractive)
            return;
        if (e.ClickCount == 2)
        {
            _autoFrame = true;
            e.Handled = true;
            return;
        }

        _dragStart = e.GetPosition(this);
        _dragCenter = _center;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is not { } start)
            return;
        var delta = e.GetPosition(this) - start;
        if (_autoFrame && Math.Abs(delta.X) + Math.Abs(delta.Y) < 3)
            return;
        _autoFrame = false;
        _center = _dragCenter - new Vector2((float)delta.X, (float)delta.Y) / _zoom;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragStart = null;
    }

    private void StartIfVisible()
    {
        if (_animating || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _animating = true;
        _lastFrame = 0;
        RequestFrame(top);
    }

    private void RequestFrame(TopLevel top)
    {
        if (_framePending)
            return;
        _framePending = true;
        top.RequestAnimationFrame(_onAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _framePending = false;
        if (!_animating)
            return;
        if (!IsEffectivelyVisible || TopLevel.GetTopLevel(this) is not { } top)
        {
            _animating = false;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var due = MaxFramesPerSecond <= 0 || _lastFrame == 0 || Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds >= 1 / MaxFramesPerSecond - 0.002;
        if (due)
        {
            _lastFrame = now;
            Player?.AdvanceTo(now);
            BuildFrame();
            InvalidateVisual();
        }

        RequestFrame(top);
    }

    private void BuildFrame()
    {
        var size = Bounds.Size;
        if (PreviewRenderer is not { IsDisposed: false } renderer || size.Width < 1 || size.Height < 1)
            return;
        var player = Player;
        if (player is not null && !ReferenceEquals(player.Settings, _lastSettings))
        {
            _lastSettings = player.Settings;
            _settingsVersion++;
        }

        UpdateFraming(player, size);
        var scaling = (float)(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        var pixels = new Vector2(MathF.Max(1, MathF.Round((float)size.Width * scaling)), MathF.Max(1, MathF.Round((float)size.Height * scaling)));
        var frame = _frames.BeginWrite();
        frame.Begin(new Camera2D(_center, _zoom * scaling), pixels, ClearColor(), player?.Time ?? 0, renderer.Renderer.WhiteTexture);
        if (BackgroundMode == PreviewBackground.Checker)
            DrawChecker(frame, renderer.Renderer.WhiteTexture, pixels, 12 * scaling);
        var drawn = player is null ? 0 : ParticleFrameWriter.Draw(frame, renderer.Assets, player.Simulation, player.Settings, ref _instances);
        player?.ReportDrawn(drawn);
        _frames.Publish(frame);
    }

    private void UpdateFraming(ParticlePreviewPlayer? player, Size size)
    {
        if (!_autoFrame || player is null)
            return;
        var extent = MathF.Max(24, player.Simulation.ShapeExtent(player.Settings));
        var area = new Rect2(-extent, -extent, extent * 2, extent * 2);
        if (player.Bounds is { } bounds && bounds.Width < 1e5f && bounds.Height < 1e5f)
            area = area.Union(bounds);
        if (!IsInteractive)
            area = _framedArea = _framedArea.Union(area);
        var target = area.Center;
        var margin = IsInteractive ? 1.25f : 1.05f;
        var fit = MathF.Min((float)size.Width / (area.Width * margin), (float)size.Height / (area.Height * margin));
        var zoom = Math.Clamp(fit, 0.1f, 3f);
        if (_snapFraming)
        {
            _snapFraming = false;
            _center = target;
            _zoom = zoom;
            return;
        }

        const float Follow = 0.08f;
        _center = Vector2.Lerp(_center, target, Follow);
        _zoom += (zoom - _zoom) * Follow;
    }

    private Color ClearColor() => BackgroundMode switch
    {
        PreviewBackground.Light => LightBackground,
        PreviewBackground.Checker => CheckerLight,
        PreviewBackground.Scene => SceneColor,
        _ => DarkBackground
    };

    private static void DrawChecker(RenderFrame frame, Texture white, Vector2 pixels, float cell)
    {
        var columns = (int)MathF.Ceiling(pixels.X / cell);
        var rows = (int)MathF.Ceiling(pixels.Y / cell);
        var source = new Rect2(0, 0, 1, 1);
        for (var y = 0; y < rows; y++)
        {
            for (var x = y % 2; x < columns; x += 2)
                frame.Draw(white, null, SpriteInstance.Create(new Vector2(x * cell, y * cell), new Vector2(cell, cell), source, CheckerDark), RenderLayers.Background, space: RenderSpace.Screen);
        }
    }

    private Vector2 WorldToScreen(Vector2 world)
    {
        var half = new Vector2((float)Bounds.Width, (float)Bounds.Height) * 0.5f;
        return (world - _center) * _zoom + half;
    }

    private Vector2 ScreenToWorld(Point screen)
    {
        var half = new Vector2((float)Bounds.Width, (float)Bounds.Height) * 0.5f;
        return (new Vector2((float)screen.X, (float)screen.Y) - half) / _zoom + _center;
    }

    private void DrawOverlays(DrawingContext context, Rect bounds)
    {
        if (ShowGrid)
            DrawGrid(context, bounds);
        if (Player is not { } player)
            return;
        if (ShowShape)
        {
            var origin = WorldToScreen(Vector2.Zero);
            context.DrawLine(OriginPen, new Point(origin.X - 5, origin.Y), new Point(origin.X + 5, origin.Y));
            context.DrawLine(OriginPen, new Point(origin.X, origin.Y - 5), new Point(origin.X, origin.Y + 5));
            if (player.Settings.Shape.Enabled)
                context.DrawGeometry(null, ShapePen, ShapeGeometry(player, bounds.Size));
        }

        if (ShowBounds && player.Bounds is { } area)
        {
            var min = WorldToScreen(area.Position);
            var max = WorldToScreen(area.Position + area.Size);
            context.DrawRectangle(null, BoundsPen, new Rect(new Point(min.X, min.Y), new Point(max.X, max.Y)).Intersect(bounds.Inflate(2)));
        }
    }

    private StreamGeometry ShapeGeometry(ParticlePreviewPlayer player, Size size)
    {
        var shape = player.Settings.Shape;
        var key = (shape, shape.Kind, _center, _zoom, size, _settingsVersion);
        if (_shapeGeometry is null || key != _shapeKey)
        {
            _shapeKey = key;
            _shapeGeometry = ShapeOutline.Build(shape, WorldToScreen, 40 / MathF.Max(_zoom, 0.05f));
        }

        return _shapeGeometry;
    }

    private void DrawGrid(DrawingContext context, Rect bounds)
    {
        var step = 32f;
        while (step * _zoom < 14)
            step *= 2;
        while (step * _zoom > 80)
            step /= 2;
        var light = BackgroundMode is PreviewBackground.Light or PreviewBackground.Checker;
        var minor = light ? GridMinorLight : GridMinorDark;
        var axis = light ? GridAxisLight : GridAxisDark;
        var topLeft = ScreenToWorld(bounds.TopLeft);
        var bottomRight = ScreenToWorld(bounds.BottomRight);
        for (var x = MathF.Floor(topLeft.X / step) * step; x <= bottomRight.X; x += step)
        {
            var sx = Math.Round(WorldToScreen(new Vector2(x, 0)).X) + 0.5;
            context.DrawLine(MathF.Abs(x) < step * 0.01f ? axis : minor, new Point(sx, 0), new Point(sx, bounds.Height));
        }

        for (var y = MathF.Floor(topLeft.Y / step) * step; y <= bottomRight.Y; y += step)
        {
            var sy = Math.Round(WorldToScreen(new Vector2(0, y)).Y) + 0.5;
            context.DrawLine(MathF.Abs(y) < step * 0.01f ? axis : minor, new Point(0, sy), new Point(bounds.Width, sy));
        }
    }

    private void DrawFrame(ImmediateDrawingContext context, ParticlePreviewRenderer renderer, Rect bounds, PixelSize pixels)
    {
        lock (SkiaRenderThread.Gate)
        {
            if (!renderer.IsDisposed)
                DrawFrameLocked(context, renderer, bounds, pixels);
        }
    }

    private void DrawFrameLocked(ImmediateDrawingContext context, ParticlePreviewRenderer renderer, Rect bounds, PixelSize pixels)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature || _frames.BeginRead() is not { } frame)
            return;
        try
        {
            using var lease = leaseFeature.Lease();
            SkiaRenderThread.Drain();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            canvas.Scale((float)(bounds.Width / pixels.Width), (float)(bounds.Height / pixels.Height));
            renderer.Renderer.Render(frame, canvas, new SKSizeI(pixels.Width, pixels.Height));
            canvas.Restore();
        }
        finally
        {
            _frames.EndRead(frame);
        }
    }

    private sealed class DrawOperation(ParticlePreviewCanvas owner, ParticlePreviewRenderer renderer, Rect bounds, PixelSize pixels) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context) => owner.DrawFrame(context, renderer, bounds, pixels);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
