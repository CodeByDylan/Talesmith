using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Lighting;

/// <summary>Preset trade-offs between how lighting looks and what it costs.</summary>
public enum LightingQuality
{
    Low,
    Medium,
    High,

    /// <summary>Uses <see cref="LightingEnvironment.CustomQuality"/>.</summary>
    Custom
}

/// <summary>The limits and resolutions behind a <see cref="LightingQuality"/>.</summary>
/// <param name="ResolutionScale">The light map's size relative to the screen, from 0.1 to 1; lighting is smooth, so a fraction is usually enough.</param>
/// <param name="MaxLights">Visible lights drawn per frame; the brightest and nearest are kept.</param>
/// <param name="MaxShadowedLights">Lights that cast shadows per frame; others still light the scene without shadows.</param>
/// <param name="ShadowResolution">Directions per light in its shadow map; higher values give sharper, steadier shadow edges.</param>
/// <param name="ShadowSamples">Shadow map samples per pixel for soft edges; 1 gives hard shadows.</param>
/// <param name="MaxShadowCasters">Shadow caster components considered per frame, nearest to the camera first; tile map shadows are not counted.</param>
public sealed record LightingQualitySettings(float ResolutionScale, int MaxLights, int MaxShadowedLights, int ShadowResolution, int ShadowSamples,
    int MaxShadowCasters)
{
    public static LightingQualitySettings Low { get; } = new(0.25f, 16, 4, 256, 1, 64);

    public static LightingQualitySettings Medium { get; } = new(0.5f, 32, 8, 512, 5, 256);

    public static LightingQualitySettings High { get; } = new(1, 64, 16, 1024, 9, 1024);
}

/// <summary>A scene's lighting: ambient light, which layers are lit, quality and tile map shadows.</summary>
/// <remarks>
/// <para>One instance exists per scene and belongs to the game thread; hosts change it through <c>Game.Post</c>, for example from a graphics
/// settings menu.</para>
/// <para>With the defaults, a scene without lights or <see cref="Emissive"/> sprites renders exactly as without lighting: the ambient light
/// is white at full intensity, so lighting only takes effect once a scene adds lights or darkens the ambient light.</para>
/// </remarks>
public sealed class LightingEnvironment
{
    /// <summary>Turns lighting off for the scene when false.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The color of the light everywhere, before lights add to it.</summary>
    public Color AmbientColor { get; set; } = Color.White;

    /// <summary>How bright the ambient light is; 1 shows colors as drawn, 0 is complete darkness.</summary>
    public float AmbientIntensity { get; set; } = 1;

    /// <summary>Render layers below this are lit; this layer and those above, such as overlays and debug views, are drawn unlit.</summary>
    public int LitLayerLimit { get; set; } = RenderLayers.Overlay;

    public LightingQuality Quality { get; set; } = LightingQuality.Medium;

    /// <summary>The settings used when <see cref="Quality"/> is <see cref="LightingQuality.Custom"/>.</summary>
    public LightingQualitySettings CustomQuality { get; set; } = LightingQualitySettings.Medium;

    /// <summary>Whether collision layers of tile maps cast shadows: each solid cell, or the collision shapes of its tile.</summary>
    public bool TileMapShadows { get; set; } = true;

    /// <summary>The shadow caster layer, from 0 to 31, tile map shadows are on; see <see cref="Light2D.ShadowLayers"/>.</summary>
    public int TileMapShadowLayer { get; set; }

    /// <summary>How far beyond the screen, in world units, lights and shadow casters are still considered.</summary>
    public float CullingMargin { get; set; } = 32;

    /// <summary>The settings of the current <see cref="Quality"/>.</summary>
    public LightingQualitySettings QualitySettings => Quality switch
    {
        LightingQuality.Low => LightingQualitySettings.Low,
        LightingQuality.High => LightingQualitySettings.High,
        LightingQuality.Custom => CustomQuality,
        _ => LightingQualitySettings.Medium
    };

    /// <summary>The ambient color multiplied by its intensity.</summary>
    public Vector3 Ambient
    {
        get
        {
            var color = AmbientColor.ToVector4();
            return new Vector3(color.X, color.Y, color.Z) * Math.Max(0, AmbientIntensity);
        }
    }
}
