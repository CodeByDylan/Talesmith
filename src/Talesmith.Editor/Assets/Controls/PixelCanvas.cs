using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace Talesmith.Editor.Assets.Controls;

/// <summary>A labelled rectangle drawn over a <see cref="PixelCanvas"/>, in image pixels.</summary>
public sealed record CanvasRegion(Rect Bounds, string? Label = null, bool IsSelected = false, bool IsMuted = false);

/// <summary>A zoomable, pannable view of an image with sharp pixels, a checkerboard behind transparency and optional regions on top.</summary>
/// <remarks>The wheel zooms around the pointer, the middle or right button pans (the left button too, on empty space, when
/// <see cref="PanWithLeftButton"/>), and double-clicking fits the image. Derived controls draw and edit overlays in image pixels with
/// <see cref="ToImage"/> and <see cref="ToScreen(Rect)"/>.</remarks>
public class PixelCanvas : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<PixelCanvas, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<PixelCanvas, double>(nameof(Zoom), 1, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<CanvasRegion>?> RegionsProperty =
        AvaloniaProperty.Register<PixelCanvas, IReadOnlyList<CanvasRegion>?>(nameof(Regions));

    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<PixelCanvas, IBrush?>(nameof(Accent), Brushes.MediumSlateBlue);

    public static readonly StyledProperty<bool> ShowPixelGridProperty = AvaloniaProperty.Register<PixelCanvas, bool>(nameof(ShowPixelGrid), true);

    public static readonly StyledProperty<bool> PanWithLeftButtonProperty = AvaloniaProperty.Register<PixelCanvas, bool>(nameof(PanWithLeftButton), true);

    private const double MinZoom = 0.05;
    private const double MaxZoom = 64;

    private Point _center;
    private bool _autoFit = true;
    private PixelSize _lastSize;
    private Point? _panStart;
    private Point _panCenter;
    private ImageBrush? _checker;
    private ThemeVariant? _checkerTheme;

    static PixelCanvas()
    {
        FocusableProperty.OverrideDefaultValue<PixelCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<PixelCanvas>(true);
        AffectsRender<PixelCanvas>(SourceProperty, ZoomProperty, RegionsProperty, AccentProperty, ShowPixelGridProperty);
    }

    /// <summary>The image, whose pixels are the coordinates of regions and overlays.</summary>
    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Screen pixels per image pixel.</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public IReadOnlyList<CanvasRegion>? Regions
    {
        get => GetValue(RegionsProperty);
        set => SetValue(RegionsProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <summary>Whether lines between pixels show when zoomed in far.</summary>
    public bool ShowPixelGrid
    {
        get => GetValue(ShowPixelGridProperty);
        set => SetValue(ShowPixelGridProperty, value);
    }

    public bool PanWithLeftButton
    {
        get => GetValue(PanWithLeftButtonProperty);
        set => SetValue(PanWithLeftButtonProperty, value);
    }

    /// <summary>The size of <see cref="Source"/> in pixels, or empty.</summary>
    public PixelSize ImageSize => Source?.PixelSize ?? default;

    /// <summary>Where the image is drawn, in control coordinates.</summary>
    protected Rect ImageRect
    {
        get
        {
            var size = ImageSize;
            var width = size.Width * Zoom;
            var height = size.Height * Zoom;
            return new Rect(Math.Round(_center.X - width / 2), Math.Round(_center.Y - height / 2), width, height);
        }
    }

    /// <summary>Scales and centers the image to fit.</summary>
    public void ZoomToFit()
    {
        _autoFit = true;
        var size = ImageSize;
        if (size.Width == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var zoom = Math.Min((Bounds.Width - 32) / size.Width, (Bounds.Height - 32) / size.Height);
        zoom = zoom >= 1 ? Math.Floor(zoom) : zoom;
        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _center = new Rect(Bounds.Size).Center;
        InvalidateVisual();
    }

    public void ZoomToActual()
    {
        _autoFit = false;
        Zoom = 1;
        _center = new Rect(Bounds.Size).Center;
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => ZoomAround(new Rect(Bounds.Size).Center, Zoom * factor);

    /// <summary>Converts a point in control coordinates to image pixels.</summary>
    public Point ToImage(Point point)
    {
        var rect = ImageRect;
        return new Point((point.X - rect.X) / Zoom, (point.Y - rect.Y) / Zoom);
    }

    /// <summary>Converts a rectangle in image pixels to control coordinates.</summary>
    public Rect ToScreen(Rect image)
    {
        var rect = ImageRect;
        return new Rect(rect.X + image.X * Zoom, rect.Y + image.Y * Zoom, image.Width * Zoom, image.Height * Zoom);
    }

    public Point ToScreen(Point image)
    {
        var rect = ImageRect;
        return new Point(rect.X + image.X * Zoom, rect.Y + image.Y * Zoom);
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source)
            return;
        var rect = ImageRect;
        context.FillRectangle(Checker(), rect);
        var interpolation = Zoom >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality;
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = interpolation }))
            context.DrawImage(source, new Rect(source.Size), rect);
        context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), 1), rect.Inflate(0.5));
        if (ShowPixelGrid && Zoom >= 10)
            DrawPixelGrid(context, rect);
        if (Regions is { Count: > 0 } regions)
            DrawRegions(context, regions);
        RenderOverlay(context);
    }

    /// <summary>Draws on top of the image and regions.</summary>
    protected virtual void RenderOverlay(DrawingContext context)
    {
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && ImageSize != _lastSize)
        {
            _lastSize = ImageSize;
            ZoomToFit();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_autoFit)
            ZoomToFit();
        else
            _center += new Vector((e.NewSize.Width - e.PreviousSize.Width) / 2, (e.NewSize.Height - e.PreviousSize.Height) / 2);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var factor = Math.Pow(1.25, e.Delta.Y);
        var next = Zoom * factor;
        if (Zoom < 1 && next > 1 || Zoom > 1 && next < 1)
            next = 1;
        ZoomAround(e.GetPosition(this), next);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2 && !HandlesDoubleClick)
        {
            ZoomToFit();
            e.Handled = true;
            return;
        }

        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed || (point.Properties.IsLeftButtonPressed && PanWithLeftButton))
        {
            _panStart = point.Position;
            _panCenter = _center;
            e.Pointer.Capture(this);
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Handled = true;
        }
    }

    /// <summary>Whether derived controls use double-clicks, so they do not fit the image.</summary>
    protected virtual bool HandlesDoubleClick => false;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_panStart is { } start)
        {
            _autoFit = false;
            _center = _panCenter + (e.GetPosition(this) - start);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_panStart is null)
            return;
        _panStart = null;
        e.Pointer.Capture(null);
        Cursor = null;
        e.Handled = true;
    }

    /// <summary>Whether a pan with the middle or right button is in progress.</summary>
    protected bool IsPanning => _panStart is not null;

    private void ZoomAround(Point anchor, double zoom)
    {
        _autoFit = false;
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        _center = anchor + (_center - anchor) * (zoom / Zoom);
        Zoom = zoom;
    }

    private void DrawPixelGrid(DrawingContext context, Rect rect)
    {
        var visible = rect.Intersect(new Rect(Bounds.Size));
        if (visible.Width <= 0 || visible.Height <= 0)
            return;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(36, 128, 128, 128)), 1);
        var size = ImageSize;
        var firstX = Math.Max(0, (int)((visible.Left - rect.X) / Zoom));
        var lastX = Math.Min(size.Width, (int)Math.Ceiling((visible.Right - rect.X) / Zoom));
        var firstY = Math.Max(0, (int)((visible.Top - rect.Y) / Zoom));
        var lastY = Math.Min(size.Height, (int)Math.Ceiling((visible.Bottom - rect.Y) / Zoom));
        for (var x = firstX; x <= lastX; x++)
        {
            var sx = Math.Round(rect.X + x * Zoom) + 0.5;
            context.DrawLine(pen, new Point(sx, visible.Top), new Point(sx, visible.Bottom));
        }

        for (var y = firstY; y <= lastY; y++)
        {
            var sy = Math.Round(rect.Y + y * Zoom) + 0.5;
            context.DrawLine(pen, new Point(visible.Left, sy), new Point(visible.Right, sy));
        }
    }

    private void DrawRegions(DrawingContext context, IReadOnlyList<CanvasRegion> regions)
    {
        var accent = Accent ?? Brushes.MediumSlateBlue;
        var normal = new Pen(accent, 1);
        var selected = new Pen(accent, 2);
        var muted = new Pen(new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)), 1) { DashStyle = DashStyle.Dash };
        var fill = new SolidColorBrush(accent is ISolidColorBrush solid ? solid.Color : Colors.SlateBlue, 0.16);
        var labels = regions.Count <= 1024;
        foreach (var region in regions)
        {
            var rect = ToScreen(region.Bounds);
            if (region.IsSelected)
                context.FillRectangle(fill, rect);
            context.DrawRectangle(region.IsMuted ? muted : region.IsSelected ? selected : normal, rect.Deflate(0.5));
            if (labels && region.Label is { Length: > 0 } label && rect.Width >= 30 && rect.Height >= 18)
                DrawChip(context, label, new Point(rect.X + 2, rect.Y + 2), region.IsSelected ? accent : new SolidColorBrush(Color.FromArgb(190, 20, 22, 28)), rect.Width - 4);
        }
    }

    /// <summary>Draws a small label with a background, such as a region's name or a size readout.</summary>
    protected static void DrawChip(DrawingContext context, string text, Point origin, IBrush background, double maxWidth = double.PositiveInfinity)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.White)
        {
            MaxTextWidth = Math.Max(1, maxWidth - 8),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var chip = new Rect(origin.X, origin.Y, formatted.Width + 8, formatted.Height + 2);
        context.DrawRectangle(background, null, chip, 3, 3);
        context.DrawText(formatted, new Point(chip.X + 4, chip.Y + 1));
    }

    private ImageBrush Checker()
    {
        var theme = ActualThemeVariant;
        if (_checker is not null && _checkerTheme == theme)
            return _checker;
        var dark = theme == ThemeVariant.Dark;
        var a = dark ? Color.FromRgb(46, 49, 58) : Color.FromRgb(255, 255, 255);
        var b = dark ? Color.FromRgb(36, 39, 46) : Color.FromRgb(228, 231, 236);
        var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            static int Pack(Color c) => unchecked((int)(0xFF000000u | (uint)c.R << 16 | (uint)c.G << 8 | c.B));
            System.Runtime.InteropServices.Marshal.Copy(new[] { Pack(a), Pack(b) }, 0, buffer.Address, 2);
            System.Runtime.InteropServices.Marshal.Copy(new[] { Pack(b), Pack(a) }, 0, buffer.Address + buffer.RowBytes, 2);
        }

        _checker = new ImageBrush(bitmap) { TileMode = TileMode.Tile, DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute), Stretch = Stretch.Fill };
        _checkerTheme = theme;
        return _checker;
    }
}
