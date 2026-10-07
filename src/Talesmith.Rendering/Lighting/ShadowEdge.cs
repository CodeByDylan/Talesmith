using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Rendering.Lighting;

/// <summary>One edge of an occluder outline in world space.</summary>
/// <remarks>Outlines run clockwise as seen on screen (Y down), so the outside of the shape is to the left of each edge's direction.</remarks>
public readonly record struct ShadowEdge(Vector2 Start, Vector2 End)
{
    /// <summary>Whether a point lies on the outside of the edge, so the edge faces it.</summary>
    public bool Faces(Vector2 point)
    {
        var direction = End - Start;
        return direction.Y * (point.X - Start.X) - direction.X * (point.Y - Start.Y) > 0;
    }
}

/// <summary>A run of <see cref="LightingFrame.Edges"/> that belongs to one occluder, such as a shadow caster or one chunk of a tile map.</summary>
/// <param name="Layers">The occluder layers the edges are on, as a bit mask matched against <see cref="FrameLight.ShadowLayers"/>.</param>
/// <param name="SelfShadows">Whether the shape darkens itself; otherwise its shadow starts where light leaves it, and its inside stays lit even where
/// other shapes shadow it. Touching shapes that do not shadow themselves act as one.</param>
public readonly record struct OccluderGroup(int FirstEdge, int EdgeCount, Rect2 Bounds, uint Layers, bool SelfShadows);
