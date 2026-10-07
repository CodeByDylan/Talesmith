using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>Draws a cell and its neighbors to preview a grid's shape, orientation and proportions.</summary>
public sealed class GridPreview : Control
{
    public static readonly StyledProperty<GridKind> KindProperty = AvaloniaProperty.Register<GridPreview, GridKind>(nameof(Kind));

    public static readonly StyledProperty<double> CellWidthProperty = AvaloniaProperty.Register<GridPreview, double>(nameof(CellWidth), 64);

    public static readonly StyledProperty<double> CellHeightProperty = AvaloniaProperty.Register<GridPreview, double>(nameof(CellHeight), 74);

    static GridPreview() => AffectsRender<GridPreview>(KindProperty, CellWidthProperty, CellHeightProperty);

    public GridKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double CellWidth
    {
        get => GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var layout = GridGeometry.Layout(Kind, CellWidth, CellHeight);
        var cells = new List<GridCoord>();
        layout.Topology.Range(GridCoord.Zero, 1, cells);
        var (scale, origin) = GridGeometry.Fit(layout, cells, Bounds.Size, 0.92);
        var accent = SelectTool.AccentColor();
        var stroke = new Pen(new SolidColorBrush(accent), 1.5, lineJoin: PenLineJoin.Round);
        var fill = new SolidColorBrush(accent, 0.14);
        var center = new SolidColorBrush(accent, 0.38);
        foreach (var cell in cells)
            context.DrawGeometry(cell == GridCoord.Zero ? center : fill, stroke, GridGeometry.Cell(layout, cell, scale, origin));
    }
}
