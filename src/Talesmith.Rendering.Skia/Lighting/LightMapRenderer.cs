using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Rendering.Skia.Lighting;

/// <summary>Draws a frame's lights into a reduced-resolution light map and multiplies the lit part of the scene by it.</summary>
/// <remarks>
/// <para>The light map stores half the light, cleared to half the ambient light: a half-float surface on GPU canvases, 8-bit on CPU raster
/// canvases. On GPUs each light is one quad shaded by <see cref="LightingShaders.Light"/> with the frame's shadow map rows and occluder
/// mask as child shaders; on the CPU, where SkSL is slow, <see cref="GeometryLightPainter"/> draws them with built-in gradients and
/// paths. Glowing sprites add their alpha masks.</para>
/// <para><see cref="Composite"/> multiplies the scene by twice the map, scaled to the viewport.</para>
/// </remarks>
internal sealed class LightMapRenderer : IDisposable
{
    private const int UniformSize = 96;

    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear);
    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest);

    private readonly TextureStore _textures;
    private readonly QuadVertices _quads;
    private readonly int _whiteTexture;
    private readonly ShadowMapBuilder _shadows = new();
    private readonly OccluderMaskBuilder _occluders = new();
    private readonly SKRuntimeEffect _lightEffect;
    private readonly SKBlender _composite;
    private readonly SKColorFilter _alphaToWhite = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.SrcIn);
    private readonly Dictionary<TextureSlot, MaskShader> _maskShaders = [];
    private readonly SKPaint _paint = new() { IsAntialias = false };
    private readonly GeometryLightPainter _geometry;
    private readonly byte[] _uniforms = new byte[UniformSize];
    private readonly nint[] _children = new nint[3];
    private readonly nint _noOccluders = SkiaNative.sk_shader_new_color4f(new SKColorF(0, 0, 0, 0), 0);
    private readonly SpriteInstance[] _quad = new SpriteInstance[1];
    private Half[] _shadowPixels = [];
    private GCHandle _shadowPixelsHandle;
    private SKSurface? _surface;
    private SKSizeI _surfaceSize;
    private GRRecordingContext? _context;
    private nint _lightImage;
    private nint _lightShader;
    private bool _onCpu;

    public LightMapRenderer(TextureStore textures, QuadVertices quads, Texture whiteTexture)
    {
        _textures = textures;
        _quads = quads;
        _whiteTexture = whiteTexture.Id;
        _geometry = new GeometryLightPainter(textures);
        _lightEffect = SKRuntimeEffect.CreateShader(LightingShaders.Light, out var error)
            ?? throw new InvalidOperationException($"The built-in light shader does not compile: {error}");
        if (_lightEffect.UniformSize != UniformSize || _lightEffect.Children.Count != 3)
            throw new InvalidOperationException("The built-in light shader does not have the expected uniforms.");
        var blender = SKRuntimeEffect.CreateBlender(LightingShaders.Composite, out error)
            ?? throw new InvalidOperationException($"The built-in light composite blender does not compile: {error}");
        _composite = blender.ToBlender();
        blender.Dispose();
    }

    /// <summary>Draws the frame's light map when its lighting is active, and returns whether it did.</summary>
    public bool Render(RenderFrame frame, GRRecordingContext? context, SKSizeI size, long frameIndex)
    {
        ReleaseLightMap();
        var lighting = frame.Lighting;
        if (!lighting.IsActive || size.Width <= 0 || size.Height <= 0)
            return false;

        var settings = lighting.Settings;
        var mapSize = new SKSizeI(Math.Max(1, (int)MathF.Ceiling(size.Width * settings.ResolutionScale)),
            Math.Max(1, (int)MathF.Ceiling(size.Height * settings.ResolutionScale)));
        _onCpu = context is null;
        EnsureSurface(context, mapSize);

        var canvas = _surface!.Canvas;
        var ambient = settings.Ambient / LightMapSettings.MaxBrightness;
        canvas.ResetMatrix();
        canvas.Clear(new SKColorF(ambient.X, ambient.Y, ambient.Z, 1));
        var toMap = SKMatrix.CreateScale((float)mapSize.Width / size.Width, (float)mapSize.Height / size.Height);
        canvas.SetMatrix(toMap.PreConcat(SkiaRenderer.ToSkia(frame.ViewMatrix)));

        var shadowed = BuildShadowMap(lighting);
        var worldToMap = frame.ViewMatrix * Matrix3x2.CreateScale((float)mapSize.Width / size.Width, (float)mapSize.Height / size.Height);
        var occluders = shadowed ? CreateOccluderImage(lighting, worldToMap, mapSize) : 0;
        try
        {
            if (_onCpu)
                _geometry.Draw(canvas, _paint, frame, shadowed ? _shadows : null, occluders);
            else
                DrawLights(canvas, frame, shadowed, occluders);
        }
        finally
        {
            if (occluders != 0)
                SkiaNative.sk_image_unref(occluders);
        }

        DrawEmissive(canvas, lighting, frameIndex);
        _lightImage = SkiaNative.sk_surface_new_image_snapshot(_surface.Handle);
        var toScreen = SKMatrix.CreateScale((float)size.Width / mapSize.Width, (float)size.Height / mapSize.Height);
        _lightShader = SkiaNative.sk_image_make_shader(_lightImage, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, in LinearSampling, in toScreen);
        return true;
    }

    /// <summary>Multiplies everything drawn so far on <paramref name="canvas"/> by the light map from <see cref="Render"/>.</summary>
    public void Composite(SKCanvas canvas, SKMatrix screenMatrix, SKSizeI size)
    {
        if (_lightShader == 0)
            return;
        canvas.SetMatrix(screenMatrix);
        var bounds = SKRect.Create(size.Width, size.Height);
        SkiaNative.sk_paint_set_shader(_paint.Handle, _lightShader);
        if (_onCpu)
        {
            // Native blending clamps the light to 1, so multiply by the stored half, then double with a color dodge against 50% gray.
            _paint.BlendMode = SKBlendMode.Modulate;
            canvas.DrawRect(bounds, _paint);
            SkiaNative.sk_paint_set_shader(_paint.Handle, 0);
            _paint.ColorF = new SKColorF(0.5f, 0.5f, 0.5f, 1);
            _paint.BlendMode = SKBlendMode.ColorDodge;
            canvas.DrawRect(bounds, _paint);
            _paint.Color = SKColors.White;
            return;
        }

        _paint.Blender = _composite;
        canvas.DrawRect(bounds, _paint);
        _paint.Blender = null;
        SkiaNative.sk_paint_set_shader(_paint.Handle, 0);
    }

    /// <summary>Releases glow masks of textures that have not been drawn since <paramref name="frame"/>.</summary>
    public void EvictUnusedSince(long frame)
    {
        foreach (var (slot, mask) in _maskShaders)
        {
            if (mask.LastUsedFrame >= frame)
                continue;
            mask.Shader.Dispose();
            _maskShaders.Remove(slot);
        }
    }

    public void Dispose()
    {
        ReleaseLightMap();
        foreach (var mask in _maskShaders.Values)
            mask.Shader.Dispose();
        _maskShaders.Clear();
        _geometry.Dispose();
        _surface?.Dispose();
        _surface = null;
        if (_shadowPixelsHandle.IsAllocated)
            _shadowPixelsHandle.Free();
        SkiaNative.sk_shader_unref(_noOccluders);
        _paint.Dispose();
        _alphaToWhite.Dispose();
        _composite.Dispose();
        _lightEffect.Dispose();
    }

    private void DrawLights(SKCanvas canvas, RenderFrame frame, bool shadowed, nint occluders)
    {
        var shadowMap = shadowed ? CreateShadowMapImage() : default;
        // The mask is in light map pixels: undoing the canvas transform places it in the world, and each light shifts it to its own quad.
        var occluderShader = occluders != 0 && canvas.TotalMatrix.TryInvert(out var mapToWorld)
            ? SkiaNative.sk_image_make_shader(occluders, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, in NearestSampling, in mapToWorld)
            : 0;
        try
        {
            DrawLights(canvas, frame, shadowMap.Shader, occluderShader);
        }
        finally
        {
            if (occluderShader != 0)
                SkiaNative.sk_shader_unref(occluderShader);
            if (shadowMap.Shader != 0)
                SkiaNative.sk_shader_unref(shadowMap.Shader);
            if (shadowMap.Image != 0)
                SkiaNative.sk_image_unref(shadowMap.Image);
        }
    }

    private void DrawLights(SKCanvas canvas, RenderFrame frame, nint shadowMap, nint occluders)
    {
        var lighting = frame.Lighting;
        var lights = lighting.Lights;
        var settings = lighting.Settings;
        var white = _textures.Find(_whiteTexture)!;
        var canvasHandle = canvas.Handle;
        var paintHandle = _paint.Handle;
        var mapSize = new Vector2(_shadows.Width, _shadows.RowCount);
        var pixelsPerUnit = frame.Camera.Zoom * settings.ResolutionScale;
        _children[1] = shadowMap != 0 ? shadowMap : white.Shader.Handle;

        for (var i = 0; i < lights.Length; i++)
        {
            ref readonly var light = ref lights[i];
            if (light.Radius <= 0)
                continue;
            var row = shadowMap != 0 ? _shadows.RowOf(i) : -1;
            var cookie = light.Cookie.IsNone ? null : _textures.Find(light.Cookie.Id);
            var data = LightShaderData.For(light with { Cookie = cookie is null ? default : light.Cookie },
                row < 0 ? -1 : _shadows.RowCoordinate(row), settings, pixelsPerUnit);
            MemoryMarshal.Write(_uniforms, data);
            MemoryMarshal.Write(_uniforms.AsSpan(80),
                cookie is null ? Vector2.One : new Vector2(cookie.Image.Width, cookie.Image.Height));
            MemoryMarshal.Write(_uniforms.AsSpan(88), mapSize);
            _children[0] = (cookie ?? white).Shader.Handle;
            // Light quads have the offset from the light as local coordinates.
            var lightOccluders = occluders == 0 ? 0
                : SkiaNative.sk_shader_with_local_matrix(occluders, SKMatrix.CreateTranslation(-light.Position.X, -light.Position.Y));
            _children[2] = lightOccluders != 0 ? lightOccluders : _noOccluders;

            var uniforms = SkiaNative.sk_data_new_with_copy(in _uniforms[0], UniformSize);
            var shader = SkiaNative.sk_runtimeeffect_make_shader(_lightEffect.Handle, uniforms, in _children[0], _children.Length, 0);
            SkiaNative.sk_data_unref(uniforms);
            if (lightOccluders != 0)
                SkiaNative.sk_shader_unref(lightOccluders);

            _quad[0] = LightShaderData.Quad(light, frame.VisibleBounds);
            var vertices = _quads.Create(_quad);
            SkiaNative.sk_paint_set_shader(paintHandle, shader);
            _paint.BlendMode = light.Blend switch
            {
                LightBlend.Multiply => SKBlendMode.Modulate,
                LightBlend.Mix => SKBlendMode.SrcOver,
                _ => SKBlendMode.Plus
            };
            SkiaNative.sk_canvas_draw_vertices(canvasHandle, vertices, SKBlendMode.Modulate, paintHandle);
            SkiaNative.sk_vertices_unref(vertices);
            SkiaNative.sk_shader_unref(shader);
        }

        SkiaNative.sk_paint_set_shader(paintHandle, 0);
    }

    private void DrawEmissive(SKCanvas canvas, LightingFrame lighting, long frameIndex)
    {
        var instances = lighting.EmissiveInstances;
        _paint.BlendMode = SKBlendMode.Plus;
        foreach (ref readonly var batch in lighting.EmissiveBatches)
        {
            if (_textures.Find(batch.Texture.Id) is not { } slot)
                continue;
            if (!_maskShaders.TryGetValue(slot, out var mask))
            {
                mask = new MaskShader(slot.Shader.WithColorFilter(_alphaToWhite));
                _maskShaders.Add(slot, mask);
            }

            mask.LastUsedFrame = frameIndex;
            var vertices = _quads.Create(instances.Slice(batch.FirstInstance, batch.InstanceCount));
            SkiaNative.sk_paint_set_shader(_paint.Handle, mask.Shader.Handle);
            SkiaNative.sk_canvas_draw_vertices(canvas.Handle, vertices, SKBlendMode.Modulate, _paint.Handle);
            SkiaNative.sk_vertices_unref(vertices);
        }

        SkiaNative.sk_paint_set_shader(_paint.Handle, 0);
    }

    /// <summary>Builds the shadow map rows when a light casts shadows and there is something to cast them; returns whether it did.</summary>
    private bool BuildShadowMap(LightingFrame lighting)
    {
        var any = false;
        foreach (ref readonly var light in lighting.Lights)
            any |= light.CastsShadows;
        if (!any || lighting.Occluders.IsEmpty)
            return false;
        _shadows.Build(lighting);
        return true;
    }

    /// <summary>Builds the mask of the occluders that stay lit, as an alpha image the size of the light map, or 0 when they cover none of it.</summary>
    private nint CreateOccluderImage(LightingFrame lighting, in Matrix3x2 worldToMap, SKSizeI mapSize)
    {
        if (!_occluders.Build(lighting, worldToMap, mapSize.Width, mapSize.Height))
            return 0;
        var info = new SkiaNative.ImageInfo(mapSize.Width, mapSize.Height, SkiaNative.ImageInfo.Alpha8, SKAlphaType.Premul);
        return SkiaNative.sk_image_new_raster_copy(in info, in MemoryMarshal.GetReference(_occluders.Data), mapSize.Width);
    }

    /// <summary>Wraps the shadow map rows in a nearest-sampled image shader; both handles are 0 when the image cannot be made.</summary>
    private (nint Image, nint Shader) CreateShadowMapImage()
    {
        var data = _shadows.Data;
        var texels = data.Length;
        if (_shadowPixels.Length < texels * 4)
        {
            if (_shadowPixelsHandle.IsAllocated)
                _shadowPixelsHandle.Free();
            _shadowPixels = new Half[Math.Max(texels * 4, _shadowPixels.Length * 2)];
            _shadowPixelsHandle = GCHandle.Alloc(_shadowPixels, GCHandleType.Pinned);
            for (var i = 3; i < _shadowPixels.Length; i += 4)
                _shadowPixels[i] = Half.One;
        }

        for (var i = 0; i < texels; i++)
            _shadowPixels[i * 4] = data[i];

        var info = new SkiaNative.ImageInfo(_shadows.Width, _shadows.RowCount, SkiaNative.ImageInfo.RgbaF16, SKAlphaType.Premul);
        var image = SkiaNative.sk_image_new_raster_copy(in info, _shadowPixelsHandle.AddrOfPinnedObject(), _shadows.Width * 8);
        if (image == 0)
            return default;
        var shader = SkiaNative.sk_image_make_shader(image, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, in NearestSampling, 0);
        return (image, shader);
    }

    private void EnsureSurface(GRRecordingContext? context, SKSizeI size)
    {
        if (_surface is not null && _surfaceSize == size && ReferenceEquals(_context, context))
            return;

        _surface?.Dispose();
        _surface = context is null
            // On the CPU, half floats make upscaling the map several times slower; additive lights leave alpha meaningless, so it is ignored.
            ? CreateSurface(context, new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            : CreateSurface(context, new SKImageInfo(size.Width, size.Height, SKColorType.RgbaF16, SKAlphaType.Premul))
              ?? CreateSurface(context, new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException($"Skia could not create a {size.Width}×{size.Height} light map.");
        _surfaceSize = size;
        _context = context;
    }

    private static SKSurface? CreateSurface(GRRecordingContext? context, SKImageInfo info) =>
        context is null ? SKSurface.Create(info) : SKSurface.Create(context, false, info);

    private void ReleaseLightMap()
    {
        if (_lightShader != 0)
            SkiaNative.sk_shader_unref(_lightShader);
        if (_lightImage != 0)
            SkiaNative.sk_image_unref(_lightImage);
        _lightShader = 0;
        _lightImage = 0;
    }

    private sealed class MaskShader(SKShader shader)
    {
        public readonly SKShader Shader = shader;
        public long LastUsedFrame;
    }
}
