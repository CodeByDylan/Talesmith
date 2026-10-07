using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.Mathematics;

namespace Talesmith.Rendering.Lighting;

/// <summary>A light packed into the five <c>float4</c> uniforms the backends' light shaders read, in std140 layout.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Color"/>: the light's color (halved for the light map, except for multiply lights), and its <see cref="LightBlend"/>.</item>
/// <item><see cref="Shape"/>: radius, inner radius fraction, falloff exponent and <see cref="LightType"/>.</item>
/// <item><see cref="Cone"/>: direction, and the cosines of half the outer and inner cone angles.</item>
/// <item><see cref="Shadow"/>: the shadow map row coordinate (negative without shadows), strength, softness and sample count.</item>
/// <item><see cref="Extra"/>: shadow map width, normalized directional shadow length (0 is unlimited), whether a cookie is set and light map pixels per world unit, which places the dither of soft shadow samples.</item>
/// </list>
/// Light quads are drawn as sprites whose texture coordinates are the offset from the light in world units.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct LightShaderData
{
    public Vector4 Color;
    public Vector4 Shape;
    public Vector4 Cone;
    public Vector4 Shadow;
    public Vector4 Extra;

    public static LightShaderData For(in FrameLight light, float shadowRow, in LightMapSettings settings, float pixelsPerUnit)
    {
        var color = light.Blend == LightBlend.Multiply ? light.Color : light.Color / LightMapSettings.MaxBrightness;
        var outer = Math.Clamp(light.SpotAngle, 0.001f, MathF.Tau) / 2;
        var inner = Math.Clamp(light.SpotInnerAngle, 0, light.SpotAngle) / 2;
        var cosOuter = light.Type == LightType.Spot ? MathF.Cos(outer) : -2;
        var cosInner = light.Type == LightType.Spot ? MathF.Max(MathF.Cos(inner), cosOuter + 1e-3f) : -1;
        var length = light.Type == LightType.Directional && light.ShadowLength > 0 ? light.ShadowLength / (2 * light.Radius) : 0;
        return new LightShaderData
        {
            Color = new Vector4(color, (float)light.Blend),
            Shape = new Vector4(light.Radius, Math.Clamp(light.InnerRadius, 0, 0.999f), MathF.Max(0.01f, light.Falloff), (float)light.Type),
            Cone = new Vector4(light.Direction, cosOuter, cosInner),
            Shadow = new Vector4(shadowRow, Math.Clamp(light.ShadowStrength, 0, 1), Math.Clamp(light.ShadowSoftness, 0, 1), settings.ShadowSamples),
            Extra = new Vector4(settings.ShadowResolution, length, light.Cookie.IsNone ? 0 : 1, pixelsPerUnit)
        };
    }

    /// <summary>The quad a light is drawn with: its bounds, or the visible area for directional lights, with offsets from the light as source.</summary>
    public static SpriteInstance Quad(in FrameLight light, in Rect2 visibleBounds)
    {
        var area = light.Type == LightType.Directional ? visibleBounds : light.Bounds;
        var transform = Matrix3x2.CreateScale(area.Size) * Matrix3x2.CreateTranslation(area.Position);
        return new SpriteInstance(transform, area.Offset(-light.Position), Mathematics.Color.White);
    }
}
