using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.Mathematics;

namespace Talesmith.Rendering;

/// <summary>One textured quad to draw: where it goes, which part of the texture it shows and how it is tinted.</summary>
/// <remarks>
/// <see cref="Transform"/> maps the unit square, (0, 0) to (1, 1), onto world space, so it includes the quad's size, origin, rotation
/// and any flip. The layout is fixed because backends upload instances to the GPU as-is.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct SpriteInstance
{
    public Matrix3x2 Transform;

    /// <summary>The source rectangle in texture pixels.</summary>
    public Rect2 Source;

    /// <summary>A straight-alpha color the texture is multiplied by; white leaves it unchanged.</summary>
    public Color Tint;

    public SpriteInstance(in Matrix3x2 transform, in Rect2 source, Color tint)
    {
        Transform = transform;
        Source = source;
        Tint = tint;
    }

    /// <summary>Creates a sprite of <paramref name="size"/> world units placed at <paramref name="position"/>.</summary>
    /// <param name="origin">The point of the sprite, from (0, 0) top-left to (1, 1) bottom-right, that sits on <paramref name="position"/>.</param>
    /// <param name="rotation">Clockwise rotation in radians around <paramref name="origin"/>.</param>
    /// <param name="flipX">Mirrors the sprite horizontally around <paramref name="origin"/>.</param>
    /// <param name="flipY">Mirrors the sprite vertically around <paramref name="origin"/>.</param>
    public static SpriteInstance Create(Vector2 position, Vector2 size, in Rect2 source, Color tint, Vector2 origin = default,
        float rotation = 0, bool flipX = false, bool flipY = false)
    {
        var scale = new Vector2(flipX ? -size.X : size.X, flipY ? -size.Y : size.Y);
        var transform = Matrix3x2.CreateTranslation(-origin) * Matrix3x2.CreateScale(scale);
        if (rotation != 0)
            transform *= Matrix3x2.CreateRotation(rotation);
        transform *= Matrix3x2.CreateTranslation(position);
        return new SpriteInstance(transform, source, tint);
    }

    /// <summary>The axis-aligned world bounds of the quad, for culling.</summary>
    public readonly Rect2 Bounds => new Rect2(0, 0, 1, 1).Transform(Transform);
}
