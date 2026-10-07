using System.Numerics;

namespace Talesmith.Physics.Geometry;

/// <summary>Turns collider descriptions into convex shapes in the space of their entity.</summary>
internal static class ColliderShapes
{
    /// <summary>Adds the convex shapes of a collider, offset, turned and scaled, to <paramref name="output"/>.</summary>
    /// <exception cref="ArgumentException">The collider has no area or its polygon crosses itself.</exception>
    public static void Build(in Collider2D collider, Vector2 scale, List<Shape> output) =>
        Build(collider.Shape, collider.Size, collider.Radius, collider.Points, collider.Offset, collider.Rotation, scale, output);

    public static void Build(in QueryShape shape, List<Shape> output) =>
        Build(shape.Kind, shape.Size, shape.Radius, shape.Points, Vector2.Zero, 0, Vector2.One, output);

    /// <summary>The centered shape before offset, rotation and scale, or <see langword="null"/> for polygons, which may have several pieces.</summary>
    public static Shape? Primitive(ColliderShape kind, Vector2 size, float radius)
    {
        switch (kind)
        {
            case ColliderShape.Box:
                if (size.X <= 0 || size.Y <= 0)
                    throw new ArgumentException("A box collider needs a positive width and height.");
                return Shape.Box(size * 0.5f);
            case ColliderShape.Circle:
                if (radius <= 0)
                    throw new ArgumentException("A circle collider needs a positive radius.");
                return Shape.Circle(Vector2.Zero, radius);
            case ColliderShape.Capsule:
            {
                if (size.X <= 0 || size.Y <= 0)
                    throw new ArgumentException("A capsule collider needs a positive width and height.");
                var vertical = size.Y >= size.X;
                var r = (vertical ? size.X : size.Y) * 0.5f;
                var half = (vertical ? size.Y : size.X) * 0.5f - r;
                var axis = vertical ? new Vector2(0, half) : new Vector2(half, 0);
                return Shape.Capsule(-axis, axis, r);
            }
            default:
                return null;
        }
    }

    private static void Build(ColliderShape kind, Vector2 size, float radius, Vector2[]? points, Vector2 offset, float rotation, Vector2 scale, List<Shape> output)
    {
        var xf = new Xf(offset, rotation);
        if (Primitive(kind, size, radius) is { } primitive)
        {
            output.Add(primitive.Transform(xf).Scale(scale));
            return;
        }

        if (points is null || points.Length < 3)
            throw new ArgumentException("A polygon collider needs at least three points.");
        Span<Vector2> transformed = stackalloc Vector2[Shape.MaxVertices];
        if (points.Length <= Shape.MaxVertices && PolygonTools.IsConvex(points))
        {
            for (var i = 0; i < points.Length; i++)
                transformed[i] = xf.Apply(points[i]) * scale;
            output.Add(Shape.Polygon(transformed[..points.Length]));
            return;
        }

        foreach (var piece in PolygonTools.Decompose(points))
        {
            for (var i = 0; i < piece.Length; i++)
                transformed[i] = xf.Apply(piece[i]) * scale;
            output.Add(Shape.Polygon(transformed[..piece.Length]));
        }
    }
}
