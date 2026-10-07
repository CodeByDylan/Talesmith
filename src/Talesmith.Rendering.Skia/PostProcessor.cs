using System.Numerics;
using SkiaSharp;

namespace Talesmith.Rendering.Skia;

/// <summary>Renders a frame offscreen and runs its post effects over it, the last one drawing onto the destination.</summary>
internal sealed class PostProcessor(ShaderLibrary library) : IDisposable
{
    private static readonly SKSamplingOptions SceneSampling = new(SKFilterMode.Nearest);

    private readonly Vector4[] _parameters = new Vector4[4];
    private (RuntimeShader Shader, PostEffectInstance Effect)[] _active = new (RuntimeShader, PostEffectInstance)[4];
    private SKSurface? _source;
    private SKSurface? _target;
    private SKSizeI _size;
    private GRRecordingContext? _context;
    private int _count;

    /// <summary>Collects the frame's usable effects and returns the canvas to draw the scene on, or null when there are none.</summary>
    public SKCanvas? Begin(RenderFrame frame, GRRecordingContext? context, SKSizeI size)
    {
        _count = 0;
        var effects = frame.PostEffects;
        for (var i = 0; i < effects.Count; i++)
        {
            var shader = library.Get(effects[i].Shader);
            if (shader is null)
                continue;
            if (_count == _active.Length)
                Array.Resize(ref _active, _count * 2);
            _active[_count++] = (shader, effects[i]);
        }

        if (_count == 0 || size.Width <= 0 || size.Height <= 0)
            return null;

        EnsureSurfaces(context, size);
        return _source!.Canvas;
    }

    /// <summary>Applies the effects collected by <see cref="Begin"/> and returns the number of draw calls.</summary>
    public int Apply(SKCanvas destination, SKPaint paint, float time)
    {
        var resolution = new Vector2(_size.Width, _size.Height);
        var bounds = SKRect.Create(_size.Width, _size.Height);
        paint.BlendMode = SKBlendMode.Src;
        for (var i = 0; i < _count; i++)
        {
            var (compiled, effect) = _active[i];
            _parameters[0] = effect.P0;
            _parameters[1] = effect.P1;
            _parameters[2] = effect.P2;
            _parameters[3] = effect.P3;

            var scene = SkiaNative.sk_surface_new_image_snapshot(_source!.Handle);
            var sceneShader = SkiaNative.sk_image_make_shader(scene, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, in SceneSampling, 0);
            var shader = compiled.CreateShader(sceneShader, _parameters, resolution, time);
            SkiaNative.sk_paint_set_shader(paint.Handle, shader);
            (i == _count - 1 ? destination : _target!.Canvas).DrawRect(bounds, paint);
            SkiaNative.sk_paint_set_shader(paint.Handle, 0);
            SkiaNative.sk_shader_unref(shader);
            SkiaNative.sk_shader_unref(sceneShader);
            SkiaNative.sk_image_unref(scene);
            (_source, _target) = (_target, _source);
        }

        return _count;
    }

    public void Dispose()
    {
        _source?.Dispose();
        _target?.Dispose();
        _source = _target = null;
    }

    private void EnsureSurfaces(GRRecordingContext? context, SKSizeI size)
    {
        if (_source is not null && _size == size && ReferenceEquals(_context, context))
            return;

        Dispose();
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _source = CreateSurface(context, info);
        _target = CreateSurface(context, info);
        _size = size;
        _context = context;
    }

    private static SKSurface CreateSurface(GRRecordingContext? context, SKImageInfo info) =>
        (context is null ? SKSurface.Create(info) : SKSurface.Create(context, false, info))
        ?? throw new InvalidOperationException($"Skia could not create a {info.Width}×{info.Height} offscreen surface for post effects.");
}
