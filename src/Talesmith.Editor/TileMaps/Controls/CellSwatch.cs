using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.Grids;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>A cell of the map's grid filled with a brush, like Hexy's swatches: a regular hexagon on hex grids and a square on square grids.</summary>
public sealed class CellSwatch : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<CellSwatch, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<CellSwatch, IBrush?>(nameof(Stroke));

    /// <summary>The grid whose cell shape is drawn; inherited, so setting it on a view makes every swatch inside follow the edited map.</summary>
    public static readonly AttachedProperty<GridKind> KindProperty =
        AvaloniaProperty.RegisterAttached<CellSwatch, Control, GridKind>("Kind", GridKind.HexPointyTop, inherits: true);

    static CellSwatch() => AffectsRender<CellSwatch>(FillProperty, StrokeProperty, KindProperty);

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public GridKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static GridKind GetKind(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return control.GetValue(KindProperty);
    }

    public static void SetKind(Control control, GridKind value)
    {
        ArgumentNullException.ThrowIfNull(control);
        control.SetValue(KindProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Shape(Kind, new Rect(Bounds.Size).Deflate(0.5)) is { } geometry)
            context.DrawGeometry(Fill, Stroke is null ? null : new Pen(Stroke, 1), geometry);
    }

    /// <summary>The outline of one cell of a grid fitted into <paramref name="area"/>, centered.</summary>
    public static StreamGeometry? Shape(GridKind kind, Rect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
            return null;
        var center = area.Center;
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        if (kind == GridKind.Square)
        {
            var side = Math.Min(area.Width, area.Height);
            context.BeginFigure(new Point(center.X - side / 2, center.Y - side / 2), true);
            context.LineTo(new Point(center.X + side / 2, center.Y - side / 2));
            context.LineTo(new Point(center.X + side / 2, center.Y + side / 2));
            context.LineTo(new Point(center.X - side / 2, center.Y + side / 2));
            context.EndFigure(true);
            return geometry;
        }

        var pointy = kind == GridKind.HexPointyTop;
        var radius = pointy ? Math.Min(area.Width / Math.Sqrt(3), area.Height / 2) : Math.Min(area.Width / 2, area.Height / Math.Sqrt(3));
        for (var i = 0; i < 6; i++)
        {
            var angle = Math.PI / 3 * i + (pointy ? Math.PI / 6 : 0);
            var point = new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            if (i == 0)
                context.BeginFigure(point, true);
            else
                context.LineTo(point);
        }

        context.EndFigure(true);
        return geometry;
    }
}
