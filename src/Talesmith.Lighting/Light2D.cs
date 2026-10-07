using Talesmith.Authoring;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Lighting;

/// <summary>A 2D light at the entity's <see cref="Runtime.Components.Transform"/>; spot and directional lights point along its rotation.</summary>
/// <remarks>Lights only brighten layers below the scene's <see cref="LightingEnvironment.LitLayerLimit"/>; overlays stay as drawn.</remarks>
[Component("Light 2D", Category = "Lighting", Icon = "lightbulb", Description = "A point, spot or directional light with optional soft shadows.")]
public struct Light2D
{
    public Light2D()
    {
    }

    /// <summary>Turns the light on and off at runtime without removing it.</summary>
    public bool Enabled = true;

    public LightType Type = LightType.Point;

    [Tooltip("Additive adds light; Mix replaces the light underneath with this color; Multiply darkens or tints it.")]
    public LightBlend Blend = LightBlend.Additive;

    public Color Color = Color.White;

    [Range(0, 8, Step = 0.05)]
    public float Intensity = 1;

    [Header("Shape")]
    [Range(0, double.PositiveInfinity, Step = 1)]
    [Tooltip("How far the light reaches in world units. Directional lights reach everywhere.")]
    public float Radius = 256;

    [Range(0, 1, Step = 0.01)]
    [Tooltip("The part of the radius lit at full intensity, from the center.")]
    public float InnerRadius;

    [Range(0.1, 8, Step = 0.05)]
    [Tooltip("How quickly the light fades toward its radius; 1 is linear, higher values concentrate it near the center.")]
    public float Falloff = 1.6f;

    [Angle]
    [Range(0, Math.Tau)]
    [Label("Spot Angle")]
    [Tooltip("The full width of a spot light's cone.")]
    public float SpotAngle = MathF.PI / 3;

    [Angle]
    [Range(0, Math.Tau)]
    [Label("Spot Inner Angle")]
    [Tooltip("The part of the cone lit at full intensity; the light fades from here to the spot angle.")]
    public float SpotInnerAngle = MathF.PI / 6;

    [Tooltip("An image the light shines through, stretched across its radius and turned with it.")]
    [AssetFilter(".png", ".jpg", ".jpeg", ".webp")]
    public Texture Cookie;

    [Header("Shadows")]
    public bool CastsShadows;

    [Range(0, 1, Step = 0.01)]
    public float ShadowStrength = 1;

    [Range(0, 1, Step = 0.01)]
    [Tooltip("How soft shadow edges are; the penumbra widens with the distance behind the shadow caster.")]
    public float ShadowSoftness = 0.4f;

    [Range(0, double.PositiveInfinity, Step = 1)]
    [Tooltip("How far a directional light's shadows reach behind their caster in world units; 0 is unlimited.")]
    public float ShadowLength;

    [Tooltip("The shadow caster layers that block this light, as a bit mask; clear a bit to let the light shine through casters on that layer.")]
    [LayerMask(LayerSet.ShadowCasters)]
    public uint ShadowLayers = uint.MaxValue;

    [Header("Animation")]
    [Tooltip("Plays in Preview and Play.")]
    public LightAnimation Animation;

    [Range(0, 20, Step = 0.1)]
    [Tooltip("Cycles per second for Pulse; how fast a flicker changes.")]
    public float AnimationSpeed = 1;

    [Range(0, 1, Step = 0.01)]
    [Tooltip("How much of the intensity the animation takes away at its lowest.")]
    public float AnimationAmount = 0.3f;

    /// <summary>Seconds of animation played, advanced by <see cref="Systems.LightAnimationSystem"/>; 0 shows the light unanimated.</summary>
    [Transient]
    public float AnimationTime;
}

/// <summary>A built-in animation of a light's intensity.</summary>
public enum LightAnimation
{
    None,

    /// <summary>Random, candle-like changes.</summary>
    Flicker,

    /// <summary>A smooth rise and fall.</summary>
    Pulse
}
