using System.Numerics;
using Talesmith.Authoring;

namespace Talesmith.Lighting;

/// <summary>A shape that blocks light and casts shadows, placed relative to the entity's <see cref="Runtime.Components.Transform"/>.</summary>
/// <remarks>The shape turns and scales with the transform. Only lights with <see cref="Light2D.CastsShadows"/> set are blocked.</remarks>
[Component("Shadow Caster 2D", Category = "Lighting", Icon = "contrast", Description = "Blocks light from shadow-casting lights.")]
public struct ShadowCaster2D
{
    public ShadowCaster2D()
    {
    }

    public bool Enabled = true;

    public ShadowCasterShape Shape = ShadowCasterShape.Box;

    [Tooltip("The size of a box in world units.")]
    public Vector2 Size = new(32, 32);

    [Range(0, double.PositiveInfinity, Step = 0.5)]
    [Tooltip("The radius of a circle in world units.")]
    public float Radius = 16;

    [Tooltip("Moves the shape from the entity's position.")]
    public Vector2 Offset;

    [Tooltip("The outline of a polygon, relative to the entity's position.")]
    public Vector2[]? Points;

    [Label("Self Shadows")]
    [Tooltip("Darkens the caster itself; otherwise the shape stays lit, even in other casters' shadows, and its shadow starts behind it.")]
    public bool SelfShadows;

    [Range(0, 31, Step = 1)]
    [Layer(LayerSet.ShadowCasters)]
    [Tooltip("The layer lights see this caster on; see Light2D.ShadowLayers.")]
    public int Layer;
}

public enum ShadowCasterShape
{
    Box,
    Circle,
    Polygon
}
