using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Rendering.Lighting;

/// <summary>How a frame's light map is drawn and where it applies.</summary>
/// <param name="Ambient">The light everywhere before lights are added, as a color multiplied by its intensity; white leaves colors unchanged.</param>
/// <param name="LitLayerLimit">Draws on layers below this are lit; this layer and those above, such as overlays and debug views, are not.</param>
/// <param name="ResolutionScale">The light map's size relative to the viewport, from 0.1 to 1.</param>
/// <param name="ShadowResolution">The number of directions (or columns, for directional lights) each light's shadow map stores.</param>
/// <param name="ShadowSamples">Shadow map samples per pixel for soft edges; 1 gives hard shadows.</param>
public readonly record struct LightMapSettings(Vector3 Ambient, int LitLayerLimit, float ResolutionScale, int ShadowResolution, int ShadowSamples)
{
    /// <summary>The settings lighting starts from: white ambient light, lit layers below <see cref="RenderLayers.Overlay"/>, a half-size light map.</summary>
    public static LightMapSettings Default { get; } = new(Vector3.One, RenderLayers.Overlay, 0.5f, 512, 5);

    /// <summary>The most a light map can brighten a color; light maps store half the light so they fit in normalized targets.</summary>
    public const float MaxBrightness = 2;
}

/// <summary>A sprite drawn into the light map as glowing mask, sharing <see cref="LightingFrame.EmissiveInstances"/> by texture.</summary>
public readonly record struct EmissiveBatch(Texture Texture, int FirstInstance, int InstanceCount);

/// <summary>The lighting of one <see cref="RenderFrame"/>: ambient light, lights, occluder outlines and glowing sprites.</summary>
/// <remarks>
/// <para>Lighting is off until <see cref="Enable"/> is called for the frame, and renderers skip it entirely while
/// <see cref="IsActive"/> is false.</para>
/// <para>Renderers draw the lights into a light map, then multiply every draw below <see cref="LightMapSettings.LitLayerLimit"/> by it
/// before drawing the layers above. Lights are drawn in the order added; add them grouped by <see cref="LightBlend"/>. Buffers are
/// reused, so filling a frame does not allocate once they have grown.</para>
/// </remarks>
public sealed class LightingFrame
{
    private FrameLight[] _lights = new FrameLight[32];
    private ShadowEdge[] _edges = new ShadowEdge[512];
    private OccluderGroup[] _occluders = new OccluderGroup[64];
    private SpriteInstance[] _emissive = new SpriteInstance[64];
    private EmissiveBatch[] _emissiveBatches = new EmissiveBatch[16];
    private int _lightCount;
    private int _edgeCount;
    private int _occluderCount;
    private int _emissiveCount;
    private int _emissiveBatchCount;

    /// <summary>Whether lighting was enabled for this frame.</summary>
    public bool IsEnabled { get; private set; }

    public LightMapSettings Settings { get; private set; } = LightMapSettings.Default;

    /// <summary>Whether lighting changes the frame: it is enabled and has lights, glowing sprites or an ambient light other than white.</summary>
    public bool IsActive => IsEnabled && (_lightCount > 0 || _emissiveCount > 0 || Settings.Ambient != Vector3.One);

    public ReadOnlySpan<FrameLight> Lights => _lights.AsSpan(0, _lightCount);

    public ReadOnlySpan<ShadowEdge> Edges => _edges.AsSpan(0, _edgeCount);

    public ReadOnlySpan<OccluderGroup> Occluders => _occluders.AsSpan(0, _occluderCount);

    /// <summary>Glowing sprites; their tints hold the light they add, already scaled for the light map.</summary>
    public ReadOnlySpan<SpriteInstance> EmissiveInstances => _emissive.AsSpan(0, _emissiveCount);

    public ReadOnlySpan<EmissiveBatch> EmissiveBatches => _emissiveBatches.AsSpan(0, _emissiveBatchCount);

    /// <summary>Turns lighting on for this frame.</summary>
    public void Enable(in LightMapSettings settings)
    {
        Settings = settings with
        {
            ResolutionScale = Math.Clamp(settings.ResolutionScale, 0.1f, 1),
            ShadowResolution = Math.Clamp(settings.ShadowResolution, 64, 4096),
            ShadowSamples = Math.Clamp(settings.ShadowSamples, 1, 16)
        };
        IsEnabled = true;
    }

    public void AddLight(in FrameLight light)
    {
        if (_lightCount == _lights.Length)
            Array.Resize(ref _lights, _lights.Length * 2);
        _lights[_lightCount++] = light;
    }

    /// <summary>Adds a closed outline as an occluder; the points may run either way around.</summary>
    /// <param name="layers">The occluder layers of the outline, as a bit mask.</param>
    public void AddOccluder(ReadOnlySpan<Vector2> outline, uint layers, bool selfShadows)
    {
        if (outline.Length < 2)
            return;
        var first = _edgeCount;
        EnsureEdgeCapacity(outline.Length);
        var clockwise = SignedArea(outline) >= 0;
        var bounds = Rect2.Bounding(outline);
        for (var i = 0; i < outline.Length; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Length];
            if (a != b)
                _edges[_edgeCount++] = clockwise ? new ShadowEdge(a, b) : new ShadowEdge(b, a);
        }

        AddGroup(new OccluderGroup(first, _edgeCount - first, bounds, layers, selfShadows));
    }

    /// <summary>Adds edges that are already oriented clockwise and form closed outlines, such as cached tile map outlines.</summary>
    public void AddOccluder(ReadOnlySpan<ShadowEdge> edges, in Rect2 bounds, uint layers, bool selfShadows)
    {
        if (edges.IsEmpty)
            return;
        EnsureEdgeCapacity(edges.Length);
        edges.CopyTo(_edges.AsSpan(_edgeCount));
        AddGroup(new OccluderGroup(_edgeCount, edges.Length, bounds, layers, selfShadows));
        _edgeCount += edges.Length;
    }

    /// <summary>Makes a sprite glow: the light map is brightened by <paramref name="instance"/>'s tint where its texture is opaque.</summary>
    /// <param name="intensity">How much light it adds; 1 shows the sprite at full brightness on a black ambient, up to <see cref="LightMapSettings.MaxBrightness"/>.</param>
    public void AddEmissive(Texture texture, in SpriteInstance instance, float intensity)
    {
        if (texture.IsNone || intensity <= 0)
            return;
        var scale = Math.Min(intensity, LightMapSettings.MaxBrightness) / LightMapSettings.MaxBrightness;
        var tint = instance.Tint;
        var scaled = instance;
        scaled.Tint = new Color((byte)(tint.R * scale + 0.5f), (byte)(tint.G * scale + 0.5f), (byte)(tint.B * scale + 0.5f), tint.A);

        if (_emissiveCount == _emissive.Length)
            Array.Resize(ref _emissive, _emissive.Length * 2);
        _emissive[_emissiveCount] = scaled;
        if (_emissiveBatchCount > 0 && _emissiveBatches[_emissiveBatchCount - 1].Texture == texture)
        {
            ref var last = ref _emissiveBatches[_emissiveBatchCount - 1];
            last = last with { InstanceCount = last.InstanceCount + 1 };
        }
        else
        {
            if (_emissiveBatchCount == _emissiveBatches.Length)
                Array.Resize(ref _emissiveBatches, _emissiveBatches.Length * 2);
            _emissiveBatches[_emissiveBatchCount++] = new EmissiveBatch(texture, _emissiveCount, 1);
        }

        _emissiveCount++;
    }

    /// <summary>The index of the first batch that is not lit, or the batch count when every batch is lit.</summary>
    public int FirstUnlitBatch(ReadOnlySpan<DrawBatch> batches)
    {
        var limit = Settings.LitLayerLimit;
        for (var i = 0; i < batches.Length; i++)
        {
            if (batches[i].Layer >= limit)
                return i;
        }

        return batches.Length;
    }

    internal void Reset()
    {
        IsEnabled = false;
        Settings = LightMapSettings.Default;
        _lightCount = 0;
        _edgeCount = 0;
        _occluderCount = 0;
        _emissiveCount = 0;
        _emissiveBatchCount = 0;
    }

    private void AddGroup(in OccluderGroup group)
    {
        if (_occluderCount == _occluders.Length)
            Array.Resize(ref _occluders, _occluders.Length * 2);
        _occluders[_occluderCount++] = group;
    }

    private void EnsureEdgeCapacity(int additional)
    {
        if (_edgeCount + additional > _edges.Length)
            Array.Resize(ref _edges, Math.Max(_edges.Length * 2, _edgeCount + additional));
    }

    /// <summary>Twice the signed area; positive when the points run clockwise on screen.</summary>
    private static float SignedArea(ReadOnlySpan<Vector2> points)
    {
        var area = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area;
    }
}
