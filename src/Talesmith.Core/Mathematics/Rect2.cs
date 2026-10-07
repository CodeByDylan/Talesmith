using System.Numerics;

namespace Talesmith.Mathematics;

/// <summary>An axis-aligned rectangle in world or screen space, with Y pointing down.</summary>
public readonly record struct Rect2(float X, float Y, float Width, float Height)
{
    public static Rect2 Empty => default;

    public float Left => X;

    public float Top => Y;

    public float Right => X + Width;

    public float Bottom => Y + Height;

    public Vector2 Position => new(X, Y);

    public Vector2 Size => new(Width, Height);

    public Vector2 Center => new(X + Width / 2, Y + Height / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect2 FromEdges(float left, float top, float right, float bottom) => new(left, top, right - left, bottom - top);

    public static Rect2 FromCenter(Vector2 center, Vector2 size) => new(center.X - size.X / 2, center.Y - size.Y / 2, size.X, size.Y);

    /// <summary>The smallest rectangle containing every point.</summary>
    public static Rect2 Bounding(ReadOnlySpan<Vector2> points)
    {
        if (points.IsEmpty)
            return Empty;
        var min = points[0];
        var max = points[0];
        foreach (var point in points[1..])
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return FromEdges(min.X, min.Y, max.X, max.Y);
    }

    public bool Contains(Vector2 point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Intersects(in Rect2 other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    public Rect2 Inflate(float amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    /// <summary>The same rectangle moved by <paramref name="amount"/>.</summary>
    public Rect2 Offset(Vector2 amount) => new(X + amount.X, Y + amount.Y, Width, Height);

    public Rect2 Union(in Rect2 other) => IsEmpty ? other : other.IsEmpty ? this
        : FromEdges(MathF.Min(Left, other.Left), MathF.Min(Top, other.Top), MathF.Max(Right, other.Right), MathF.Max(Bottom, other.Bottom));

    /// <summary>The axis-aligned bounds of this rectangle after a transform.</summary>
    public Rect2 Transform(in Matrix3x2 matrix)
    {
        Span<Vector2> corners =
        [
            Vector2.Transform(new Vector2(Left, Top), matrix),
            Vector2.Transform(new Vector2(Right, Top), matrix),
            Vector2.Transform(new Vector2(Left, Bottom), matrix),
            Vector2.Transform(new Vector2(Right, Bottom), matrix)
        ];
        return Bounding(corners);
    }
}
