using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talesmith.Physics.Geometry;

/// <summary>A rotation stored as its cosine and sine; positive angles turn clockwise on screen because world Y points down.</summary>
internal readonly struct Rot(float cos, float sin)
{
    public static Rot Identity => new(1, 0);

    public readonly float Cos = cos;
    public readonly float Sin = sin;

    public static Rot FromAngle(float radians) => radians == 0 ? Identity : new Rot(MathF.Cos(radians), MathF.Sin(radians));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 Rotate(Vector2 v) => new(Cos * v.X - Sin * v.Y, Sin * v.X + Cos * v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 InverseRotate(Vector2 v) => new(Cos * v.X + Sin * v.Y, -Sin * v.X + Cos * v.Y);
}

/// <summary>A rigid transform: a rotation followed by a translation.</summary>
internal readonly struct Xf(Vector2 position, Rot rotation)
{
    public static Xf Identity => new(Vector2.Zero, Rot.Identity);

    public readonly Vector2 P = position;
    public readonly Rot Q = rotation;

    public Xf(Vector2 position, float angle) : this(position, Rot.FromAngle(angle))
    {
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vector2 Apply(Vector2 v) => Q.Rotate(v) + P;
}

/// <summary>2D cross products and small vector helpers used throughout the solver.</summary>
internal static class Vec
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>The cross product of a vector and a scalar: the vector turned by -90° and scaled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Cross(Vector2 v, float s) => new(s * v.Y, -s * v.X);

    /// <summary>The cross product of a scalar and a vector: the vector turned by 90° and scaled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Cross(float s, Vector2 v) => new(-s * v.Y, s * v.X);

    /// <summary>Normalizes <paramref name="v"/>, returning zero for vectors too short to have a direction.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Normalize(Vector2 v, out float length)
    {
        length = v.Length();
        return length < float.Epsilon ? Vector2.Zero : v / length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 Normalize(Vector2 v) => Normalize(v, out _);
}
