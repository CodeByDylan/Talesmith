using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;

namespace Talesmith.Screenshots.Scenes;

/// <summary>Stands in for the game viewport: a sky, a tile island, a sprite and a selection gizmo over an editor grid.</summary>
internal sealed class ViewportMock(Bitmap? sprite, Color accent) : Control
{
    private static readonly IBrush Sky = new ImmutableLinearGradientBrush(
        [new ImmutableGradientStop(0, Color.Parse("#2B4A73")), new ImmutableGradientStop(1, Color.Parse("#6FA8C9"))],
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative), endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    private static readonly IBrush Sea = new ImmutableSolidColorBrush(Color.Parse("#2E6F96"));
    private static readonly IBrush Grass = new ImmutableSolidColorBrush(Color.Parse("#5FA04E"));
    private static readonly IBrush GrassLight = new ImmutableSolidColorBrush(Color.Parse("#7DBF5E"));
    private static readonly IBrush Dirt = new ImmutableSolidColorBrush(Color.Parse("#8A5A3C"));
    private static readonly IBrush DirtDark = new ImmutableSolidColorBrush(Color.Parse("#6B4430"));
    private static readonly IPen GridPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White, 0.08), 1);
    private static readonly IPen AxisPen = new ImmutablePen(new ImmutableSolidColorBrush(Colors.White, 0.18), 1);

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Sky, bounds);

        const double tile = 32;
        var seaTop = Math.Round(bounds.Height * 0.72 / tile) * tile;
        context.FillRectangle(Sea, new Rect(0, seaTop, bounds.Width, bounds.Height - seaTop));

        var islandLeft = Math.Round(bounds.Width * 0.18 / tile) * tile;
        var islandWidth = Math.Round(bounds.Width * 0.5 / tile) * tile;
        var islandTop = seaTop - tile * 2;
        for (var x = islandLeft; x < islandLeft + islandWidth; x += tile)
        {
            var column = (int)((x - islandLeft) / tile);
            context.FillRectangle(Grass, new Rect(x, islandTop, tile, tile * 0.4));
            context.FillRectangle(column % 2 == 0 ? GrassLight : Grass, new Rect(x, islandTop, tile, 5));
            context.FillRectangle(Dirt, new Rect(x, islandTop + tile * 0.4, tile, tile * 3.6));
            context.FillRectangle(DirtDark, new Rect(x + (column % 3) * 7 + 4, islandTop + tile * 1.2 + (column % 2) * 20, 6, 6));
        }

        var platform = new Rect(islandLeft + islandWidth + tile * 2, islandTop - tile * 3, tile * 4, tile * 0.5);
        context.FillRectangle(Grass, platform);
        context.FillRectangle(Dirt, new Rect(platform.X, platform.Bottom, platform.Width, tile * 0.5));

        for (var x = 0.5; x < bounds.Width; x += tile)
            context.DrawLine(GridPen, new Point(x, 0), new Point(x, bounds.Height));
        for (var y = 0.5; y < bounds.Height; y += tile)
            context.DrawLine(GridPen, new Point(0, y), new Point(bounds.Width, y));
        context.DrawLine(AxisPen, new Point(islandLeft + 0.5, 0), new Point(islandLeft + 0.5, bounds.Height));

        var heroRect = new Rect(islandLeft + tile * 5, islandTop - tile * 1.5, tile * 1.5, tile * 1.5);
        if (sprite is not null)
        {
            var frame = Math.Min(sprite.PixelSize.Width, sprite.PixelSize.Height);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                context.DrawImage(sprite, new Rect(0, 0, frame, frame), heroRect);
        }

        var accentBrush = new ImmutableSolidColorBrush(accent);
        var selection = new ImmutablePen(accentBrush, 1.5);
        context.DrawRectangle(null, selection, heroRect.Inflate(3));
        var frameRect = heroRect.Inflate(3);
        foreach (var corner in (ReadOnlySpan<Point>)[frameRect.TopLeft, frameRect.TopRight, frameRect.BottomLeft, frameRect.BottomRight])
            context.DrawRectangle(Brushes.White, selection, new Rect(corner.X - 3, corner.Y - 3, 6, 6));

        var center = heroRect.Center;
        DrawArrow(context, center, new Vector(56, 0), Color.Parse("#E5534B"));
        DrawArrow(context, center, new Vector(0, -56), Color.Parse("#4FAE5C"));
        context.DrawRectangle(new ImmutableSolidColorBrush(accent, 0.35), new ImmutablePen(accentBrush, 1), new Rect(center.X + 4, center.Y - 20, 16, 16));
    }

    private static void DrawArrow(DrawingContext context, Point from, Vector direction, Color color)
    {
        var brush = new ImmutableSolidColorBrush(color);
        var to = from + direction;
        context.DrawLine(new ImmutablePen(brush, 2), from, to);
        var unit = direction / Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var normal = new Vector(-unit.Y, unit.X);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(to + unit * 10, true);
            ctx.LineTo(to + normal * 5);
            ctx.LineTo(to - normal * 5);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(brush, null, geometry);
    }
}
