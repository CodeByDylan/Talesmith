using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>Edits an auto-tile neighbor pattern by clicking the cells around a center cell, on hex and square grids alike.</summary>
/// <remarks>Each click cycles a neighbor through any (<c>*</c>), same terrain (<c>+</c>) and different terrain (<c>-</c>); right-click cycles
/// backwards.</remarks>
public sealed class NeighborPatternEditor : Control
{
    public static readonly StyledProperty<string> PatternProperty =
        AvaloniaProperty.Register<NeighborPatternEditor, string>(nameof(Pattern), "******", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IGridLayout?> LayoutProperty = AvaloniaProperty.Register<NeighborPatternEditor, IGridLayout?>(nameof(Layout));

    private int _hovered = -1;

    static NeighborPatternEditor()
    {
        AffectsRender<NeighborPatternEditor>(PatternProperty, LayoutProperty);
        CursorProperty.OverrideDefaultValue<NeighborPatternEditor>(new Cursor(StandardCursorType.Hand));
    }

    /// <summary>One character per neighbor in <see cref="GridTopology.Directions"/> order.</summary>
    public string Pattern
    {
        get => GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    /// <summary>The map's grid, which decides the cell shape and the number of neighbors.</summary>
    public IGridLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    private IGridLayout Grid => Layout ?? new HexLayout(true, 64, 74);

    protected override Size MeasureOverride(Size availableSize) => new(160, 160);

    public override void Render(DrawingContext context)
    {
        var (scale, origin) = Fit();
        var accent = SelectTool.AccentColor();
        var danger = Resource("DangerColor", Color.Parse("#EF4444"));
        var border = ResourceBrush("BorderStrongBrush", Brushes.Gray);
        var muted = ResourceBrush("TextMutedBrush", Brushes.Gray);
        var sunken = ResourceBrush("SurfaceSunkenBrush", Brushes.Transparent);
        var pattern = NormalizedPattern;
        var layout = Grid;

        context.DrawGeometry(new SolidColorBrush(accent), null, GridGeometry.Cell(layout, GridCoord.Zero, scale, origin, 0.82));
        var cellSize = Math.Min(layout.CellSize.X, layout.CellSize.Y) * scale;
        context.DrawEllipse(Brushes.White, null, origin, cellSize * 0.12, cellSize * 0.12);

        for (var d = 0; d < pattern.Length; d++)
        {
            var cell = layout.Topology.Direction(d);
            var hovered = d == _hovered;
            var geometry = GridGeometry.Cell(layout, cell, scale, origin, 0.82);
            var center = GridGeometry.ToPoint(layout.CellToWorld(cell), scale, origin);
            switch (pattern[d])
            {
                case '+':
                    context.DrawGeometry(new SolidColorBrush(accent, hovered ? 0.75 : 0.55), new Pen(new SolidColorBrush(accent), 1.5), geometry);
                    DrawGlyph(context, center, cellSize, '+', Brushes.White);
                    break;
                case '-':
                    context.DrawGeometry(new SolidColorBrush(danger, hovered ? 0.28 : 0.16), new Pen(new SolidColorBrush(danger), 1.5), geometry);
                    DrawGlyph(context, center, cellSize, '-', new SolidColorBrush(danger));
                    break;
                default:
                    context.DrawGeometry(hovered ? sunken : null, new Pen(hovered ? new SolidColorBrush(accent) : border, 1.25, new DashStyle([3, 3], 0)), geometry);
                    DrawGlyph(context, center, cellSize, '*', muted);
                    break;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var direction = DirectionAt(e.GetPosition(this));
        if (direction == _hovered)
            return;
        _hovered = direction;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hovered = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var direction = DirectionAt(e.GetPosition(this));
        if (direction < 0)
            return;
        var backwards = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
        var chars = NormalizedPattern.ToCharArray();
        chars[direction] = (chars[direction], backwards) switch
        {
            ('*', false) => '+',
            ('+', false) => '-',
            ('*', true) => '-',
            ('-', true) => '+',
            _ => '*'
        };
        Pattern = new string(chars);
        e.Handled = true;
    }

    private string NormalizedPattern
    {
        get
        {
            var count = Grid.Topology.NeighborCount;
            return Pattern is { } pattern && pattern.Length == count ? pattern : new string('*', count);
        }
    }

    private int DirectionAt(Point point)
    {
        var (scale, origin) = Fit();
        var layout = Grid;
        var cell = layout.WorldToCell(new System.Numerics.Vector2((float)((point.X - origin.X) / scale), (float)((point.Y - origin.Y) / scale)));
        for (var d = 0; d < layout.Topology.NeighborCount; d++)
        {
            if (layout.Topology.Direction(d) == cell)
                return d;
        }

        return -1;
    }

    private (double Scale, Point Origin) Fit()
    {
        var topology = Grid.Topology;
        var cells = new List<GridCoord> { GridCoord.Zero };
        for (var d = 0; d < topology.NeighborCount; d++)
            cells.Add(topology.Direction(d));
        return GridGeometry.Fit(Grid, cells, Bounds.Size, 0.98);
    }

    private static void DrawGlyph(DrawingContext context, Point center, double cellSize, char glyph, IBrush brush)
    {
        var arm = cellSize * 0.14;
        var pen = new Pen(brush, 2, lineCap: PenLineCap.Round);
        switch (glyph)
        {
            case '+':
                context.DrawLine(pen, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
                context.DrawLine(pen, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
                break;
            case '-':
                context.DrawLine(pen, new Point(center.X - arm, center.Y - arm), new Point(center.X + arm, center.Y + arm));
                context.DrawLine(pen, new Point(center.X - arm, center.Y + arm), new Point(center.X + arm, center.Y - arm));
                break;
            default:
                context.DrawEllipse(brush, null, center, 1.75, 1.75);
                break;
        }
    }

    private Color Resource(string key, Color fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is Color color ? color : fallback;

    private IBrush ResourceBrush(string key, IBrush fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
}
