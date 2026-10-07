using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>The collision polygons of a tile being edited, in pixels from the cell center.</summary>
public sealed class CollisionShapes
{
    private readonly List<List<Vector2>> _shapes;

    public CollisionShapes(IEnumerable<IReadOnlyList<Vector2>> shapes) => _shapes = [.. shapes.Select(s => s.ToList())];

    public IReadOnlyList<IReadOnlyList<Vector2>> Shapes => _shapes;

    /// <summary>The shape new vertices are added to; -1 starts a new shape.</summary>
    public int Active { get; set; } = -1;

    public event EventHandler? Changed;

    /// <summary>The shapes with at least three vertices, as tiles store them.</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Complete => [.. _shapes.Where(s => s.Count >= 3).Select(s => (IReadOnlyList<Vector2>)s.ToArray())];

    public void AddVertex(Vector2 point)
    {
        if (Active < 0 || Active >= _shapes.Count)
        {
            _shapes.Add([]);
            Active = _shapes.Count - 1;
        }

        _shapes[Active].Add(point);
        Raise();
    }

    public void MoveVertex(int shape, int vertex, Vector2 point)
    {
        _shapes[shape][vertex] = point;
        Raise();
    }

    public void RemoveVertex(int shape, int vertex)
    {
        _shapes[shape].RemoveAt(vertex);
        if (_shapes[shape].Count == 0)
        {
            _shapes.RemoveAt(shape);
            Active = -1;
        }

        Raise();
    }

    /// <summary>Finishes the active shape, so the next vertex starts another.</summary>
    public void Finish()
    {
        if (Active >= 0 && Active < _shapes.Count && _shapes[Active].Count < 3)
            _shapes.RemoveAt(Active);
        Active = -1;
        Raise();
    }

    public void Add(IReadOnlyList<Vector2> shape)
    {
        _shapes.Add([.. shape]);
        Active = -1;
        Raise();
    }

    public void Clear()
    {
        _shapes.Clear();
        Active = -1;
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Edits a tile's collision polygons over its artwork and cell outline: click adds a vertex, double-click or Enter finishes the shape,
/// dragging moves a vertex and right-click removes one. Vertices snap to whole pixels, or to cell corners and edge middles with Ctrl.</summary>
public sealed class CollisionShapeEditor : Control
{
    public static readonly StyledProperty<CollisionShapes?> ShapesProperty = AvaloniaProperty.Register<CollisionShapeEditor, CollisionShapes?>(nameof(Shapes));

    public static readonly StyledProperty<IGridLayout?> LayoutProperty = AvaloniaProperty.Register<CollisionShapeEditor, IGridLayout?>(nameof(Layout));

    public static readonly StyledProperty<IImage?> ImageProperty = AvaloniaProperty.Register<CollisionShapeEditor, IImage?>(nameof(Image));

    /// <summary>The artwork's size in pixels; it is drawn bottom-centered on the cell as on the map.</summary>
    public static readonly StyledProperty<Size> ImageSizeProperty = AvaloniaProperty.Register<CollisionShapeEditor, Size>(nameof(ImageSize));

    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<CollisionShapeEditor, IBrush?>(nameof(Fill));

    private const double HandleRadius = 5;

    private (int Shape, int Vertex)? _dragging;
    private (int Shape, int Vertex)? _hovered;

    static CollisionShapeEditor()
    {
        AffectsRender<CollisionShapeEditor>(ShapesProperty, LayoutProperty, ImageProperty, ImageSizeProperty, FillProperty);
        FocusableProperty.OverrideDefaultValue<CollisionShapeEditor>(true);
        ClipToBoundsProperty.OverrideDefaultValue<CollisionShapeEditor>(true);
    }

    public CollisionShapes? Shapes
    {
        get => GetValue(ShapesProperty);
        set => SetValue(ShapesProperty, value);
    }

    public IGridLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IImage? Image
    {
        get => GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public Size ImageSize
    {
        get => GetValue(ImageSizeProperty);
        set => SetValue(ImageSizeProperty, value);
    }

    /// <summary>The fill of color tiles, which have no artwork.</summary>
    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ShapesProperty)
            return;
        if (change.OldValue is CollisionShapes old)
            old.Changed -= OnShapesChanged;
        if (change.NewValue is CollisionShapes shapes)
            shapes.Changed += OnShapesChanged;
    }

    public override void Render(DrawingContext context)
    {
        if (Layout is not { } layout)
            return;
        var (scale, origin) = Fit(layout);
        var accent = SelectTool.AccentColor();
        var danger = Color.Parse("#EF4444");
        var outline = GridGeometry.Cell(layout, GridCoord.Zero, scale, origin);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent, 0.6), 1, new DashStyle([4, 3], 0)), outline);
        if (Image is { } image)
        {
            var size = ImageSize;
            var rect = new Rect(origin.X - size.Width / 2 * scale, origin.Y + (layout.CellSize.Y / 2 - size.Height) * scale, size.Width * scale, size.Height * scale);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                context.DrawImage(image, rect);
        }
        else if (Fill is { } fill)
        {
            context.DrawGeometry(fill, null, outline);
        }

        if (Shapes is not { } shapes)
            return;
        var shapeFill = new SolidColorBrush(danger, 0.3);
        var shapePen = new Pen(new SolidColorBrush(danger), 1.75, lineJoin: PenLineJoin.Round);
        for (var s = 0; s < shapes.Shapes.Count; s++)
        {
            var shape = shapes.Shapes[s];
            if (shape.Count == 0)
                continue;
            var geometry = new StreamGeometry();
            using (var stream = geometry.Open())
            {
                stream.BeginFigure(ToScreen(shape[0], scale, origin), shape.Count >= 3);
                for (var v = 1; v < shape.Count; v++)
                    stream.LineTo(ToScreen(shape[v], scale, origin));
                stream.EndFigure(shape.Count >= 3 && s != shapes.Active);
            }

            context.DrawGeometry(shape.Count >= 3 ? shapeFill : null, shapePen, geometry);
            for (var v = 0; v < shape.Count; v++)
            {
                var hovered = _hovered == (s, v) || _dragging == (s, v);
                context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(hovered ? accent : danger), 1.5), ToScreen(shape[v], scale, origin),
                    hovered ? HandleRadius + 1 : HandleRadius, hovered ? HandleRadius + 1 : HandleRadius);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Shapes is not { } shapes || Layout is not { } layout)
            return;
        var point = e.GetCurrentPoint(this);
        var hit = HitVertex(point.Position);
        if (point.Properties.IsRightButtonPressed)
        {
            if (hit is { } remove)
                shapes.RemoveVertex(remove.Shape, remove.Vertex);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;
        if (e.ClickCount >= 2)
        {
            shapes.Finish();
        }
        else if (hit is not null)
        {
            _dragging = hit;
            e.Pointer.Capture(this);
        }
        else
        {
            shapes.AddVertex(ToCell(point.Position, layout, e.KeyModifiers));
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Shapes is not { } shapes || Layout is not { } layout)
            return;
        var position = e.GetPosition(this);
        if (_dragging is { } dragging)
        {
            shapes.MoveVertex(dragging.Shape, dragging.Vertex, ToCell(position, layout, e.KeyModifiers));
            return;
        }

        var hovered = HitVertex(position);
        if (hovered == _hovered)
            return;
        _hovered = hovered;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging is null)
            return;
        _dragging = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && Shapes is { } shapes)
        {
            shapes.Finish();
            e.Handled = true;
        }
    }

    private void OnShapesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private (int Shape, int Vertex)? HitVertex(Point position)
    {
        if (Shapes is not { } shapes || Layout is not { } layout)
            return null;
        var (scale, origin) = Fit(layout);
        for (var s = shapes.Shapes.Count - 1; s >= 0; s--)
        {
            for (var v = 0; v < shapes.Shapes[s].Count; v++)
            {
                if (Point.Distance(ToScreen(shapes.Shapes[s][v], scale, origin), position) <= HandleRadius + 3)
                    return (s, v);
            }
        }

        return null;
    }

    private Vector2 ToCell(Point position, IGridLayout layout, KeyModifiers modifiers)
    {
        var (scale, origin) = Fit(layout);
        var point = new Vector2((float)((position.X - origin.X) / scale), (float)((position.Y - origin.Y) / scale));
        if ((modifiers & KeyModifiers.Control) == 0)
            return new Vector2(MathF.Round(point.X), MathF.Round(point.Y));
        var best = layout.CornerOffset(0);
        for (var i = 0; i < layout.CornerCount; i++)
        {
            var corner = layout.CornerOffset(i);
            var middle = (corner + layout.CornerOffset((i + 1) % layout.CornerCount)) / 2;
            foreach (var candidate in (ReadOnlySpan<Vector2>)[corner, middle, Vector2.Zero])
            {
                if (Vector2.DistanceSquared(candidate, point) < Vector2.DistanceSquared(best, point))
                    best = candidate;
            }
        }

        return new Vector2(MathF.Round(best.X * 10) / 10, MathF.Round(best.Y * 10) / 10);
    }

    private (double Scale, Point Origin) Fit(IGridLayout layout)
    {
        var halfWidth = Math.Max(layout.CellSize.X, ImageSize.Width) / 2;
        var top = Math.Min(-layout.CellSize.Y / 2, layout.CellSize.Y / 2 - ImageSize.Height);
        var bottom = layout.CellSize.Y / 2;
        var scale = Math.Max(0.01, Math.Min(Bounds.Width / (halfWidth * 2), Bounds.Height / (bottom - top)) * 0.86);
        return (scale, new Point(Bounds.Width / 2, Bounds.Height / 2 - (top + bottom) / 2 * scale));
    }

    private static Point ToScreen(Vector2 point, double scale, Point origin) => new(origin.X + point.X * scale, origin.Y + point.Y * scale);
}
