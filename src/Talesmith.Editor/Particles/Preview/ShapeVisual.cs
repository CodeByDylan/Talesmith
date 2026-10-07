using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles.Preview;

/// <summary>A small drawing of an emitter's shape and direction, scaled to fit, shown in the shape module's card.</summary>
public sealed class ShapeVisual : Control
{
    public static readonly StyledProperty<ShapeModule?> ShapeProperty = AvaloniaProperty.Register<ShapeVisual, ShapeModule?>(nameof(Shape));

    private static readonly IPen AxisPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(40, 128, 128, 128)), 1);

    static ShapeVisual() => AffectsRender<ShapeVisual>(ShapeProperty);

    public ShapeModule? Shape
    {
        get => GetValue(ShapeProperty);
        set => SetValue(ShapeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var background = this.FindResource("SurfaceSunkenBrush") as IBrush;
        context.DrawRectangle(background, null, bounds, 8, 8);
        if (Shape is not { } shape || bounds.Width < 8 || bounds.Height < 8)
            return;
        var center = bounds.Center;
        context.DrawLine(AxisPen, new Point(0, center.Y), new Point(bounds.Width, center.Y));
        context.DrawLine(AxisPen, new Point(center.X, 0), new Point(center.X, bounds.Height));

        var extent = MathF.Max(Extent(shape), 1);
        var scale = (float)(Math.Min(bounds.Width, bounds.Height) / 2 - 12) / (extent * 1.15f);
        Vector2 ToScreen(Vector2 world) => new((float)center.X + world.X * scale, (float)center.Y + world.Y * scale);
        var accent = this.FindResource("AccentBrush") as IBrush ?? Brushes.CornflowerBlue;
        var geometry = ShapeOutline.Build(shape, ToScreen, extent * 0.45f);
        context.DrawGeometry(null, new Pen(accent, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
        var origin = ToScreen(Vector2.Zero);
        context.DrawEllipse(accent, null, new Point(origin.X, origin.Y), 2.5, 2.5);
    }

    private static float Extent(ShapeModule shape)
    {
        var kind = shape.Kind switch
        {
            ParticleShapeKind.Line => MathF.Abs(shape.Size.X) * 0.5f,
            ParticleShapeKind.Rectangle => (shape.Size * 0.5f).Length(),
            ParticleShapeKind.Circle or ParticleShapeKind.Cone => MathF.Abs(shape.Radius),
            ParticleShapeKind.Tile => (shape.CellSize * 0.5f).Length(),
            ParticleShapeKind.Polygon => shape.Points.Count == 0 ? 0 : shape.Points.Max(p => p.Length()),
            _ => 0
        };
        return MathF.Max(shape.Offset.Length() + kind, 16);
    }
}
