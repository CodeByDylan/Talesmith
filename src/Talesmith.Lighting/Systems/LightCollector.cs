using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;
using Talesmith.Runtime.Components;

namespace Talesmith.Lighting.Systems;

/// <summary>Gathers the enabled lights that reach the visible area, keeps the most important within the quality's limits and orders them by blend mode.</summary>
internal sealed class LightCollector
{
    private FrameLight[] _lights = new FrameLight[32];
    private float[] _keys = new float[32];
    private int _count;

    public ReadOnlySpan<FrameLight> Lights => _lights.AsSpan(0, _count);

    /// <summary>The lights that cast shadows after <see cref="Collect"/>.</summary>
    public int ShadowedCount { get; private set; }

    /// <summary>The area the shadowed lights reach, where occluders matter.</summary>
    public Rect2 ShadowBounds { get; private set; }

    public void Collect(World world, in Rect2 visible, float margin, LightingQualitySettings quality)
    {
        _count = 0;
        var area = visible.Inflate(margin);
        var center = visible.Center;
        foreach (var archetype in world.Query<Light2D, Transform>())
        {
            var entities = archetype.Entities;
            var lights = archetype.GetSpan<Light2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < lights.Length; i++)
            {
                ref readonly var light = ref lights[i];
                if (!light.Enabled)
                    continue;
                var intensity = light.Intensity * LightAnimations.Factor(light.Animation, light.AnimationTime, light.AnimationAmount, entities[i].Id);
                if (intensity <= 0)
                    continue;
                if (ToFrameLight(light, transforms[i], intensity, area) is { } frameLight)
                    Add(frameLight, Importance(frameLight, center));
            }
        }

        if (_count > quality.MaxLights)
        {
            SortByImportance();
            _count = Math.Max(0, quality.MaxLights);
        }

        LimitShadows(quality.MaxShadowedLights);
        SortByBlend();
    }

    private static FrameLight? ToFrameLight(in Light2D light, in Transform transform, float intensity, in Rect2 area)
    {
        var color = light.Color.ToVector4();
        var frameLight = new FrameLight
        {
            Type = light.Type,
            Blend = light.Blend,
            Position = transform.Position,
            Direction = new Vector2(MathF.Cos(transform.Rotation), MathF.Sin(transform.Rotation)),
            Color = new Vector3(color.X, color.Y, color.Z) * intensity,
            Radius = light.Radius,
            InnerRadius = light.InnerRadius,
            Falloff = light.Falloff,
            SpotAngle = light.SpotAngle,
            SpotInnerAngle = light.SpotInnerAngle,
            CastsShadows = light.CastsShadows && light.ShadowStrength > 0,
            ShadowStrength = light.ShadowStrength,
            ShadowSoftness = light.ShadowSoftness,
            ShadowLayers = light.ShadowLayers,
            ShadowLength = light.ShadowLength,
            Cookie = light.Cookie
        };

        if (light.Type == LightType.Directional)
            return frameLight with { Position = area.Center, Radius = area.Size.Length() / 2 };
        if (light.Radius <= 0 || !area.Intersects(frameLight.Bounds))
            return null;
        return frameLight;
    }

    /// <summary>Brighter, larger and more central lights matter more; directional lights always come first.</summary>
    private static float Importance(in FrameLight light, Vector2 center)
    {
        if (light.Type == LightType.Directional)
            return float.MaxValue;
        var brightness = Vector3.Dot(light.Color, new Vector3(0.2126f, 0.7152f, 0.0722f));
        return brightness * light.Radius / (light.Radius + Vector2.Distance(light.Position, center));
    }

    private void Add(in FrameLight light, float importance)
    {
        if (_count == _lights.Length)
        {
            Array.Resize(ref _lights, _count * 2);
            Array.Resize(ref _keys, _count * 2);
        }

        _keys[_count] = importance;
        _lights[_count++] = light;
    }

    private void SortByImportance()
    {
        var keys = _keys.AsSpan(0, _count);
        for (var i = 0; i < keys.Length; i++)
            keys[i] = -keys[i];
        keys.Sort(_lights.AsSpan(0, _count));
        for (var i = 0; i < keys.Length; i++)
            keys[i] = -keys[i];
    }

    private void LimitShadows(int max)
    {
        var shadowed = 0;
        foreach (ref readonly var light in Lights)
        {
            if (light.CastsShadows)
                shadowed++;
        }

        if (shadowed > max)
        {
            SortByImportance();
            shadowed = 0;
            foreach (ref var light in _lights.AsSpan(0, _count))
            {
                if (light.CastsShadows && ++shadowed > max)
                    light.CastsShadows = false;
            }

            shadowed = Math.Max(0, max);
        }

        ShadowedCount = shadowed;
        var bounds = Rect2.Empty;
        foreach (ref readonly var light in Lights)
        {
            if (light.CastsShadows)
                bounds = bounds.Union(light.Bounds);
        }

        ShadowBounds = bounds;
    }

    /// <summary>Additive lights first, then mix, then multiply, keeping the current order within each.</summary>
    private void SortByBlend()
    {
        var keys = _keys.AsSpan(0, _count);
        for (var i = 0; i < keys.Length; i++)
            keys[i] = (int)_lights[i].Blend * 65536f + i;
        keys.Sort(_lights.AsSpan(0, _count));
    }
}
