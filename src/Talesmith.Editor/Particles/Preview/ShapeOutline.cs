using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Talesmith.Grids;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles.Preview;

/// <summary>Draws an emitter's shape: its outline, the inner ring of rings and arrows for the emission direction.</summary>
public static class ShapeOutline
{
    private const int CircleSegments = 48;

    /// <summary>Builds the outline in screen space.</summary>
    /// <param name="toScreen">Maps a position relative to the emitter, in world units, to the screen.</param>
    /// <param name="arrowLength">The length of direction arrows in world units.</param>
    public static StreamGeometry Build(ShapeModule shape, Func<Vector2, Vector2> toScreen, float arrowLength)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(toScreen);
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var local = Matrix3x2.CreateRotation(shape.Rotation) * Matrix3x2.CreateTranslation(shape.Offset);
        void Polyline(ReadOnlySpan<Vector2> points, bool closed, bool transform = true)
        {
            if (points.Length < 2)
                return;
            for (var i = 0; i < points.Length; i++)
            {
                var screen = toScreen(transform ? Vector2.Transform(points[i], local) : points[i]);
                if (i == 0)
                    context.BeginFigure(new Point(screen.X, screen.Y), false);
                else
                    context.LineTo(new Point(screen.X, screen.Y));
            }

            context.EndFigure(closed);
        }

        void Arrow(Vector2 from, float angle, bool transform)
        {
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var tip = from + direction * arrowLength;
            var side = new Vector2(-direction.Y, direction.X) * (arrowLength * 0.18f);
            var back = tip - direction * (arrowLength * 0.25f);
            Polyline([from, tip], false, transform);
            Polyline([back + side, tip, back - side], false, transform);
        }

        switch (shape.Kind)
        {
            case ParticleShapeKind.Line:
                var half = MathF.Abs(shape.Size.X) * 0.5f;
                Polyline([new(-half, 0), new(half, 0)], false);
                break;
            case ParticleShapeKind.Rectangle:
                var h = shape.Size * 0.5f;
                Polyline([new(-h.X, -h.Y), new(h.X, -h.Y), new(h.X, h.Y), new(-h.X, h.Y)], true);
                break;
            case ParticleShapeKind.Circle:
                Arc(shape.Radius, shape.Arc, Polyline);
                if (shape.InnerRadius > 0 && shape.EmitFrom == ParticleEmitFrom.Volume)
                    Arc(MathF.Min(shape.InnerRadius, shape.Radius), shape.Arc, Polyline);
                break;
            case ParticleShapeKind.Cone:
                var axis = new Vector2(MathF.Cos(shape.Angle), MathF.Sin(shape.Angle));
                var across = new Vector2(-axis.Y, axis.X) * shape.Radius;
                Polyline([-across, across], false);
                Arrow(-across, shape.Angle - shape.ConeAngle * 0.5f, true);
                Arrow(across, shape.Angle + shape.ConeAngle * 0.5f, true);
                break;
            case ParticleShapeKind.Tile when shape.CellSize is { X: > 0, Y: > 0 }:
                IGridLayout layout = shape.Tile == GridKind.Square
                    ? new SquareLayout(shape.CellSize.X, shape.CellSize.Y)
                    : new HexLayout(shape.Tile == GridKind.HexPointyTop, shape.CellSize.X, shape.CellSize.Y);
                var corners = new Vector2[layout.CornerCount];
                for (var i = 0; i < corners.Length; i++)
                    corners[i] = layout.CornerOffset(i);
                Polyline(corners, true);
                break;
            case ParticleShapeKind.Polygon when shape.Points.Count > 1:
                Polyline(shape.Points.ToArray(), true);
                break;
        }

        if (shape.Kind != ParticleShapeKind.Cone && shape.Direction == ParticleDirectionMode.Fixed)
            Arrow(shape.Offset, shape.Angle, false);
        return geometry;
    }

    private delegate void PolylineWriter(ReadOnlySpan<Vector2> points, bool closed, bool transform = true);

    private static void Arc(float radius, float arc, PolylineWriter polyline)
    {
        radius = MathF.Abs(radius);
        if (radius <= 0)
            return;
        arc = Math.Clamp(arc, 0, MathF.Tau);
        var full = arc >= MathF.Tau - 1e-3f;
        var segments = Math.Max(4, (int)MathF.Ceiling(CircleSegments * arc / MathF.Tau));
        Span<Vector2> points = stackalloc Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
        {
            var angle = arc * i / segments;
            points[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        polyline(full ? points[..segments] : points, full);
    }
}
