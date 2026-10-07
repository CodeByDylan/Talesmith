using System.Numerics;
using SkiaSharp;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Rendering.Skia.Lighting;

/// <summary>Draws lights with Skia's built-in gradients and paths, for CPU raster canvases where per-pixel SkSL is too slow.</summary>
/// <remarks>
/// A light's falloff is a radial gradient. Shadows and spot cones become the shape the gradient is drawn in: the visibility polygon read
/// from the light's shadow map row (or, for directional lights, the lit stretch of each column), with a blur on its edges for soft
/// shadows. Inside the occluders that stay lit, given by the <see cref="OccluderMaskBuilder"/> mask, the light is drawn unshadowed
/// instead, and the visibility polygon is masked out there so the two never add up. The result matches
/// <see cref="LightingShaders.Light"/> except that penumbrae have one width instead of widening with distance, and multiply lights
/// ignore cookies and partial shadow strength.
/// </remarks>
internal sealed class GeometryLightPainter(TextureStore textures) : IDisposable
{
    private const int FalloffStops = 16;
    private const float ShadowBias = 0.004f;

    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest);

    private readonly SKColorF[] _colors = new SKColorF[FalloffStops];
    private readonly float[] _positions = CreatePositions();
    private readonly SKPath _path = new();
    private readonly nint _white = SkiaNative.sk_shader_new_color4f(new SKColorF(1, 1, 1, 1), 0);

    /// <param name="occluders">The frame's occluder mask as an alpha image in light map pixels, or 0 when no occluder stays lit.</param>
    public void Draw(SKCanvas canvas, SKPaint paint, RenderFrame frame, ShadowMapBuilder? shadows, nint occluders)
    {
        // Undoing the canvas transform lays the mask over the light map's pixels one to one.
        var mask = occluders != 0 && canvas.TotalMatrix.TryInvert(out var mapToWorld)
            ? SkiaNative.sk_image_make_shader(occluders, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, in NearestSampling, in mapToWorld)
            : 0;
        try
        {
            var lights = frame.Lighting.Lights;
            for (var i = 0; i < lights.Length; i++)
            {
                ref readonly var light = ref lights[i];
                if (light.Radius <= 0)
                    continue;
                var row = shadows is null ? -1 : shadows.RowOf(i);
                DrawLight(canvas, paint, frame, light, row < 0 ? default : shadows!.Data.Slice(row * shadows.Width, shadows.Width), mask);
            }
        }
        finally
        {
            if (mask != 0)
                SkiaNative.sk_shader_unref(mask);
        }

        paint.Color = SKColors.White;
        SkiaNative.sk_paint_set_shader(paint.Handle, 0);
        SkiaNative.sk_paint_set_maskfilter(paint.Handle, 0);
    }

    public void Dispose()
    {
        _path.Dispose();
        SkiaNative.sk_shader_unref(_white);
    }

    /// <param name="occluders">The occluder mask as a shader in the canvas's coordinates, or 0.</param>
    private void DrawLight(SKCanvas canvas, SKPaint paint, RenderFrame frame, in FrameLight light, ReadOnlySpan<Half> shadowRow, nint occluders)
    {
        paint.BlendMode = light.Blend switch
        {
            LightBlend.Multiply => SKBlendMode.Modulate,
            LightBlend.Mix => SKBlendMode.SrcOver,
            _ => SKBlendMode.Plus
        };

        var shader = CreateShader(light);
        SkiaNative.sk_paint_set_shader(paint.Handle, shader);
        paint.Color = SKColors.White;

        var shadowed = !shadowRow.IsEmpty;
        var strength = shadowed ? Math.Clamp(light.ShadowStrength, 0, 1) : 1;
        if (shadowed && strength < 1 && light.Blend != LightBlend.Multiply)
        {
            SetAlpha(paint, 1 - strength);
            FillUnshadowed(canvas, paint, frame, light);
        }

        SetAlpha(paint, shadowed && light.Blend != LightBlend.Multiply ? strength : 1);
        var blur = MathF.Max(ShadowBlur(light), light.Type == LightType.Spot ? ConeBlur(light) : 0);
        if (!shadowed)
        {
            FillUnshadowed(canvas, paint, frame, light);
        }
        else if (occluders == 0)
        {
            FillShape(canvas, paint, light, shadowRow, blur);
        }
        else
        {
            UseShader(paint, Masked(shader, occluders, inside: false, light.Blend));
            FillShape(canvas, paint, light, shadowRow, blur);
            UseShader(paint, Masked(shader, occluders, inside: true, light.Blend));
            FillUnshadowed(canvas, paint, frame, light);
        }

        SkiaNative.sk_shader_unref(shader);
    }

    /// <summary>Fills everything the light reaches without shadows: its spot cone, or its whole area.</summary>
    private void FillUnshadowed(SKCanvas canvas, SKPaint paint, RenderFrame frame, in FrameLight light)
    {
        if (light.Type == LightType.Spot)
            FillShape(canvas, paint, light, default, ConeBlur(light));
        else
            canvas.DrawRect(ToSkia(light.Type == LightType.Directional ? frame.VisibleBounds : light.Bounds), paint);
    }

    /// <summary>A light's shader kept only inside, or only outside, the occluders that stay lit, as a new reference.</summary>
    /// <remarks>Elsewhere it adds nothing: it is transparent, or white for multiply lights, which multiply by it.</remarks>
    private nint Masked(nint light, nint occluders, bool inside, LightBlend blend)
    {
        var masked = SkiaNative.sk_shader_new_blend(inside ? SKBlendMode.DstIn : SKBlendMode.DstOut, light, occluders);
        if (blend != LightBlend.Multiply)
            return masked;
        var kept = SkiaNative.sk_shader_new_blend(SKBlendMode.SrcOver, _white, masked);
        SkiaNative.sk_shader_unref(masked);
        return kept;
    }

    /// <summary>Hands a new shader reference over to the paint.</summary>
    private static void UseShader(SKPaint paint, nint shader)
    {
        SkiaNative.sk_paint_set_shader(paint.Handle, shader);
        SkiaNative.sk_shader_unref(shader);
    }

    private void FillShape(SKCanvas canvas, SKPaint paint, in FrameLight light, ReadOnlySpan<Half> shadowRow, float blur)
    {
        _path.Rewind();
        if (light.Type == LightType.Directional)
            AddLitColumns(light, shadowRow);
        else
            AddVisibilityPolygon(light, shadowRow);

        var mask = blur > 0 ? SkiaNative.sk_maskfilter_new_blur_with_flags(SKBlurStyle.Normal, blur, true) : 0;
        SkiaNative.sk_paint_set_maskfilter(paint.Handle, mask);
        canvas.DrawPath(_path, paint);
        SkiaNative.sk_paint_set_maskfilter(paint.Handle, 0);
        if (mask != 0)
            SkiaNative.sk_maskfilter_unref(mask);
    }

    /// <summary>The area a point or spot light reaches: out to its radius, or to where its shadow starts in each direction of its shadow map
    /// row.</summary>
    private void AddVisibilityPolygon(in FrameLight light, ReadOnlySpan<Half> shadowRow)
    {
        var center = light.Position;
        var radius = light.Radius * 1.05f;
        var spot = light.Type == LightType.Spot;
        var heading = MathF.Atan2(light.Direction.Y, light.Direction.X);
        var halfCone = Math.Clamp(light.SpotAngle, 0.001f, MathF.Tau) / 2;
        var steps = shadowRow.IsEmpty ? 64 : shadowRow.Length;
        var start = spot ? heading - halfCone : -MathF.PI;
        var span = spot ? halfCone * 2 : MathF.Tau;
        var count = spot ? Math.Max(2, (int)MathF.Ceiling(steps * span / MathF.Tau) + 1) : steps;

        if (spot)
            _path.MoveTo(center.X, center.Y);
        for (var i = 0; i < count; i++)
        {
            var angle = spot ? start + span * i / (count - 1) : start + (i + 0.5f) * span / count;
            var distance = radius;
            if (!shadowRow.IsEmpty)
            {
                var s = angle / MathF.Tau + 0.5f;
                var bin = ((int)MathF.Floor(s * shadowRow.Length) % shadowRow.Length + shadowRow.Length) % shadowRow.Length;
                var depth = (float)shadowRow[bin];
                if (depth < 1)
                    distance = (depth + ShadowBias) * light.Radius;
            }

            var point = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (i == 0 && !spot)
                _path.MoveTo(point.X, point.Y);
            else
                _path.LineTo(point.X, point.Y);
        }

        _path.Close();
    }

    /// <summary>The lit stretch of every column of a directional light's area: up to its occluder, and again past the shadow's length.</summary>
    private void AddLitColumns(in FrameLight light, ReadOnlySpan<Half> shadowRow)
    {
        var width = shadowRow.Length;
        var length = light.ShadowLength > 0 ? light.ShadowLength / (2 * light.Radius) : 0;
        for (var k = 0; k < width; k++)
        {
            var depth = (float)shadowRow[k];
            var u0 = (float)k / width;
            var u1 = (float)(k + 1) / width;
            if (depth >= 1)
            {
                AddColumn(light, u0, u1, 0, 1);
                continue;
            }

            AddColumn(light, u0, u1, 0, depth + ShadowBias);
            if (length > 0 && depth + length < 1)
                AddColumn(light, u0, u1, depth + length, 1);
        }
    }

    private void AddColumn(in FrameLight light, float u0, float u1, float z0, float z1)
    {
        var across = new Vector2(-light.Direction.Y, light.Direction.X) * (2 * light.Radius);
        var along = light.Direction * (2 * light.Radius);
        var origin = light.Position - (across + along) / 2;
        Span<Vector2> corners = [origin + across * u0 + along * z0, origin + across * u1 + along * z0, origin + across * u1 + along * z1, origin + across * u0 + along * z1];
        _path.MoveTo(corners[0].X, corners[0].Y);
        for (var i = 1; i < 4; i++)
            _path.LineTo(corners[i].X, corners[i].Y);
        _path.Close();
    }

    /// <summary>The light's color as a shader: a radial gradient of its falloff, through its cookie, or one color for directional lights.</summary>
    private nint CreateShader(in FrameLight light)
    {
        var color = light.Blend == LightBlend.Multiply ? light.Color : light.Color / LightMapSettings.MaxBrightness;
        if (light.Type == LightType.Directional)
            return SkiaNative.sk_shader_new_color4f(new SKColorF(color.X, color.Y, color.Z, 1), 0);

        var inner = Math.Clamp(light.InnerRadius, 0, 0.999f);
        var falloff = MathF.Max(0.01f, light.Falloff);
        for (var i = 0; i < FalloffStops; i++)
        {
            var attenuation = MathF.Pow(Math.Clamp((1 - _positions[i]) / (1 - inner), 0, 1), falloff);
            _colors[i] = light.Blend switch
            {
                LightBlend.Multiply => new SKColorF(1 + (color.X - 1) * attenuation, 1 + (color.Y - 1) * attenuation, 1 + (color.Z - 1) * attenuation, 1),
                LightBlend.Mix => new SKColorF(color.X, color.Y, color.Z, attenuation),
                _ => new SKColorF(color.X * attenuation, color.Y * attenuation, color.Z * attenuation, 1)
            };
        }

        var center = new SKPoint(light.Position.X, light.Position.Y);
        var gradient = SkiaNative.sk_shader_new_radial_gradient_color4f(in center, light.Radius, _colors, 0, _positions, FalloffStops, SKShaderTileMode.Clamp, 0);
        if (light.Cookie.IsNone || light.Blend == LightBlend.Multiply || textures.Find(light.Cookie.Id) is not { } cookie)
            return gradient;

        var size = new Vector2(cookie.Image.Width, cookie.Image.Height);
        var placement = SKMatrix.CreateTranslation(-size.X / 2, -size.Y / 2)
            .PostConcat(SKMatrix.CreateScale(2 * light.Radius / size.X, 2 * light.Radius / size.Y))
            .PostConcat(SKMatrix.CreateRotation(MathF.Atan2(light.Direction.Y, light.Direction.X)))
            .PostConcat(SKMatrix.CreateTranslation(light.Position.X, light.Position.Y));
        var placed = SkiaNative.sk_shader_with_local_matrix(cookie.Shader.Handle, in placement);
        var combined = SkiaNative.sk_shader_new_blend(SKBlendMode.Modulate, gradient, placed);
        SkiaNative.sk_shader_unref(placed);
        SkiaNative.sk_shader_unref(gradient);
        return combined;
    }

    /// <summary>A blur matching the spread of the shader's soft shadows at a typical distance behind an occluder.</summary>
    private static float ShadowBlur(in FrameLight light) =>
        Math.Clamp(light.ShadowSoftness, 0, 1) * (light.Type == LightType.Directional ? light.ShadowLength > 0 ? light.ShadowLength * 0.15f : 24 : light.Radius * 0.04f);

    /// <summary>A blur approximating the fade between a spot light's inner and outer cone near its source.</summary>
    private static float ConeBlur(in FrameLight light) =>
        MathF.Max(0, light.SpotAngle - light.SpotInnerAngle) / 2 * light.Radius * 0.08f;

    private static void SetAlpha(SKPaint paint, float alpha)
    {
        var color = paint.ColorF;
        paint.ColorF = new SKColorF(color.Red, color.Green, color.Blue, Math.Clamp(alpha, 0, 1));
    }

    private static SKRect ToSkia(in Mathematics.Rect2 rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static float[] CreatePositions()
    {
        var positions = new float[FalloffStops];
        for (var i = 0; i < positions.Length; i++)
            positions[i] = (float)i / (positions.Length - 1);
        return positions;
    }
}
