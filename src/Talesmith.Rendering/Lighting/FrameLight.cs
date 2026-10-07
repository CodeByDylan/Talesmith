using System.Numerics;

namespace Talesmith.Rendering.Lighting;

/// <summary>The shape of a light.</summary>
public enum LightType
{
    /// <summary>Shines in every direction from a point, fading out at its radius.</summary>
    Point,

    /// <summary>A point light limited to a cone around its direction.</summary>
    Spot,

    /// <summary>Lights everything evenly from one direction, like the sun or the moon; shadows are parallel.</summary>
    Directional
}

/// <summary>How a light combines with the light already on screen.</summary>
public enum LightBlend
{
    /// <summary>Adds its light: torches, lamps, magic.</summary>
    Additive,

    /// <summary>Replaces the light underneath by its color, faded by its falloff: colored zones that ignore other lights.</summary>
    Mix,

    /// <summary>Multiplies the light underneath by its color: darkness, fog of war and colored filters.</summary>
    Multiply
}

/// <summary>One light of a frame, in world space, as renderers draw it into the light map.</summary>
public struct FrameLight
{
    public LightType Type;

    public LightBlend Blend;

    /// <summary>The light's center; for directional lights, the center of the area its shadows cover.</summary>
    public Vector2 Position;

    /// <summary>The unit direction the light points in, for spot cones, cookies and directional shadows.</summary>
    public Vector2 Direction;

    /// <summary>The color multiplied by the intensity, so components may exceed 1.</summary>
    public Vector3 Color;

    /// <summary>Where the light fades out; for directional lights, half the size of the area its shadows cover.</summary>
    public float Radius;

    /// <summary>The fraction of <see cref="Radius"/>, from 0 to 1, that is lit at full intensity.</summary>
    public float InnerRadius;

    /// <summary>The exponent of the fade from <see cref="InnerRadius"/> to <see cref="Radius"/>; 1 is linear, higher values fade faster.</summary>
    public float Falloff;

    /// <summary>The full angle of a spot light's cone in radians.</summary>
    public float SpotAngle;

    /// <summary>The full angle in radians of the part of the cone lit at full intensity.</summary>
    public float SpotInnerAngle;

    public bool CastsShadows;

    /// <summary>How dark shadows are, from 0 (invisible) to 1 (no light).</summary>
    public float ShadowStrength;

    /// <summary>How soft shadow edges are, from 0 (hard) to 1 (very soft); the penumbra widens with the distance behind the occluder.</summary>
    public float ShadowSoftness;

    /// <summary>The occluder layers that cast shadows from this light, as a bit mask.</summary>
    public uint ShadowLayers;

    /// <summary>How far directional shadows reach behind their occluder in world units; 0 is unlimited.</summary>
    public float ShadowLength;

    /// <summary>A texture whose colors multiply the light, mapped across the light's radius and turned with its direction; none for plain lights.</summary>
    public Texture Cookie;

    /// <summary>The world area the light can affect.</summary>
    public readonly Mathematics.Rect2 Bounds => Mathematics.Rect2.FromCenter(Position, new Vector2(Radius * 2));
}
