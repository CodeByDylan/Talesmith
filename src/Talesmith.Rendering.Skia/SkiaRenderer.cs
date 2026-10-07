using System.Numerics;
using SkiaSharp;
using Talesmith.Diagnostics;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering.Skia.Lighting;

namespace Talesmith.Rendering.Skia;

/// <summary>Draws <see cref="RenderFrame"/>s with Skia onto any canvas: a GPU canvas leased from a UI compositor or a raster surface.</summary>
/// <remarks>
/// Each <see cref="DrawBatch"/> becomes one <c>drawVertices</c> call with the batch texture as an image shader. Sprite meshes are kept as
/// native vertices until their version changes. <see cref="Render"/> and <see cref="RenderToImage"/> belong to one render thread;
/// texture methods and <see cref="Dispose"/> may be called from any thread, and <see cref="Dispose"/> waits for a frame being drawn.
/// </remarks>
public sealed class SkiaRenderer : IRenderer, IOffscreenRenderer
{
    private const string Backend = "Skia";

    /// <summary>Raster images have no hard limit; this matches common GPU limits so content stays portable between backends.</summary>
    private const int CpuMaxTextureSize = 16384;

    private const int EvictAfterFrames = 300;
    private const int EvictionInterval = 60;

    private static readonly ProfilerMarker FrameMarker = ProfilerMarker.Get("Render/Skia/Frame", "Rendering");
    private static readonly ProfilerMarker BatchesMarker = ProfilerMarker.Get("Render/Skia/Batches", "Rendering");
    private static readonly ProfilerMarker PostEffectsMarker = ProfilerMarker.Get("Render/Skia/Post effects", "Rendering");
    private static readonly ProfilerMarker LightingMarker = ProfilerMarker.Get("Render/Skia/Lighting", "Rendering");
    private static readonly ProfilerCounter DrawCallsCounter = ProfilerCounter.Get("Draw calls", CounterKind.PerFrame, "calls");
    private static readonly ProfilerCounter BatchesCounter = ProfilerCounter.Get("Batches", CounterKind.PerFrame, "batches");
    private static readonly ProfilerCounter SpritesCounter = ProfilerCounter.Get("Sprites", CounterKind.PerFrame, "sprites");
    private static readonly ProfilerCounter MeshInstancesCounter = ProfilerCounter.Get("Mesh instances", CounterKind.PerFrame, "instances");
    private static readonly ProfilerCounter TextureUploadsCounter = ProfilerCounter.Get("Texture uploads", CounterKind.PerFrame, "textures");

    private readonly Profiler? _profiler;
    private readonly TextureStore _textures;
    private readonly QuadVertices _quads = new();
    private readonly MeshCache _meshes;
    private readonly ShaderLibrary _materialLibrary;
    private readonly ShaderLibrary _postEffectLibrary;
    private readonly MaterialShaders _materialShaders;
    private readonly PostProcessor _postProcessor;
    private readonly LightMapRenderer _lighting;
    private readonly SKPaint _paint = new() { IsAntialias = false };
    private volatile RendererInfo _info = new(Backend, "CPU", CpuMaxTextureSize, SupportsCustomShaders: true);
    private readonly object _renderGate = new();
    private SKSurface? _offscreen;
    private SKSizeI _offscreenSize;
    private GRRecordingContext? _gpuContext;
    private int _renderThread;
    private long _frameIndex;
    private bool _disposed;

    public SkiaRenderer(SkiaRendererOptions? options = null)
    {
        options ??= new SkiaRendererOptions();
        _profiler = options.Profiler;
        _textures = new TextureStore(options.LinearSampling);
        _meshes = new MeshCache(_quads);
        _materialLibrary = new ShaderLibrary("image", allowFrameUniforms: false, options.Logger);
        _postEffectLibrary = new ShaderLibrary("scene", allowFrameUniforms: true, options.Logger);
        _materialShaders = new MaterialShaders(_materialLibrary);
        _postProcessor = new PostProcessor(_postEffectLibrary);
        WhiteTexture = _textures.Create(ImageData.Solid(1, 1, Color.White), TextureFilter.Nearest);
        _lighting = new LightMapRenderer(_textures, _quads, WhiteTexture);
    }

    /// <summary>Describes the renderer; <see cref="RendererInfo.Device"/> becomes "GPU" once a frame is drawn on a GPU-backed canvas.</summary>
    public RendererInfo Info => _info;

    public Texture WhiteTexture { get; }

    public Texture CreateTexture(ImageData image, TextureOptions options = default) => _textures.Create(image, options.Filter);

    public void UpdateTexture(Texture texture, ImageData image) => _textures.Update(texture, image);

    /// <summary>Destroys a texture; if a frame is being rendered, its image is released when that frame ends.</summary>
    public void DestroyTexture(Texture texture) => _textures.Destroy(texture);

    /// <summary>Draws a frame onto a canvas whose current transform places the render target's top-left corner.</summary>
    /// <param name="size">The render target size in pixels.</param>
    /// <exception cref="ObjectDisposedException">The renderer was disposed.</exception>
    public RenderStats Render(RenderFrame frame, SKCanvas canvas, SKSizeI size)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(canvas);
        lock (_renderGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RenderFrame(frame, canvas, size);
        }
    }

    private RenderStats RenderFrame(RenderFrame frame, SKCanvas canvas, SKSizeI size)
    {
        using var frameScope = _profiler is null ? default : _profiler.Measure(FrameMarker);
        var context = canvas.Context;
        _renderThread = Environment.CurrentManagedThreadId;
        if (context is not null)
            _gpuContext = context;
        GpuReleaseQueue.Drain(context);
        var uploads = _textures.BeginFrame();
        try
        {
            _frameIndex++;
            UpdateInfo(context);

            var view = ViewArea(frame.View, size);
            canvas.Save();
            try
            {
                if (view != SKRectI.Create(size))
                {
                    canvas.Clear(ToSkia(frame.BorderColor));
                    canvas.ClipRect(view);
                    canvas.Translate(view.Left, view.Top);
                }

                var stats = DrawView(frame, canvas, context, view.Size);
                if (_frameIndex % EvictionInterval == 0)
                {
                    _meshes.EvictUnusedSince(_frameIndex - EvictAfterFrames);
                    _materialShaders.EvictUnusedSince(_frameIndex - EvictAfterFrames);
                    _lighting.EvictUnusedSince(_frameIndex - EvictAfterFrames);
                }

                Report(stats, uploads);
                return stats;
            }
            finally
            {
                canvas.Restore();
            }
        }
        finally
        {
            _textures.EndFrame();
        }
    }

    /// <summary>Draws the scene, its lighting and its post effects onto a canvas whose transform places the view's top-left corner.</summary>
    private RenderStats DrawView(RenderFrame frame, SKCanvas canvas, GRRecordingContext? context, SKSizeI size)
    {
        var sceneCanvas = _postProcessor.Begin(frame, context, size);
        var scene = sceneCanvas ?? canvas;
        bool lit;
        using (_profiler is null ? default : _profiler.Measure(LightingMarker))
            lit = _lighting.Render(frame, context, size, _frameIndex);
        var stats = DrawScene(frame, scene, sceneCanvas is null ? canvas.TotalMatrix : SKMatrix.Identity, size, lit);
        if (sceneCanvas is null)
            return stats;

        using var postScope = _profiler is null ? default : _profiler.Measure(PostEffectsMarker);
        var effects = _postProcessor.Apply(canvas, _paint, (float)frame.Time);
        return stats with { DrawCalls = stats.DrawCalls + effects, PostEffects = effects };
    }

    /// <summary>Renders on a raster surface; call it on the render thread.</summary>
    public ImageData RenderToImage(RenderFrame frame, int width, int height)
    {
        lock (_renderGate)
        {
            var surface = RenderOnRasterSurface(frame, width, height);
            var pixels = new byte[width * height * 4];
            using (var pixmap = surface.PeekPixels())
                pixmap.GetPixelSpan().CopyTo(pixels);
            return new ImageData(width, height, pixels);
        }
    }

    /// <summary>Renders on a reused raster surface without copying the pixels out; call it on the render thread.</summary>
    public void RenderOffscreen(RenderFrame frame, int width, int height)
    {
        lock (_renderGate)
            RenderOnRasterSurface(frame, width, height).Flush();
    }

    private SKSurface RenderOnRasterSurface(RenderFrame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var size = new SKSizeI(width, height);
        if (_offscreen is null || _offscreenSize != size)
        {
            _offscreen?.Dispose();
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _offscreen = SKSurface.Create(info) ?? throw new InvalidOperationException($"Skia could not create a {width}×{height} surface.");
            _offscreenSize = size;
        }

        RenderFrame(frame, _offscreen.Canvas, size);
        return _offscreen;
    }

    /// <summary>Waits for a frame being drawn, then frees the renderer's resources.</summary>
    /// <remarks>Surfaces on a GPU context are released by the next frame drawn on that context when called from another thread.</remarks>
    public void Dispose()
    {
        lock (_renderGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_gpuContext is { } context && _renderThread != Environment.CurrentManagedThreadId)
            {
                GpuReleaseQueue.Enqueue(context, _postProcessor);
                GpuReleaseQueue.Enqueue(context, _lighting);
            }
            else
            {
                _postProcessor.Dispose();
                _lighting.Dispose();
            }

            _offscreen?.Dispose();
            _materialShaders.Dispose();
            _materialLibrary.Dispose();
            _postEffectLibrary.Dispose();
            _meshes.Dispose();
            _paint.Dispose();
            _textures.Dispose();
        }
    }

    private RenderStats DrawScene(RenderFrame frame, SKCanvas canvas, SKMatrix viewMatrix, SKSizeI size, bool lit)
    {
        using var scope = _profiler is null ? default : _profiler.Measure(BatchesMarker);
        var worldMatrix = viewMatrix.PreConcat(ToSkia(frame.ViewMatrix));
        var screenMatrix = viewMatrix.PreConcat(ToSkia(frame.ScreenMatrix));
        var instances = frame.Instances;
        var paintHandle = _paint.Handle;
        var canvasHandle = canvas.Handle;
        int drawCalls = 0, batches = 0, sprites = 0, meshInstances = 0;
        var space = (RenderSpace)byte.MaxValue;

        canvas.Save();
        canvas.Clear(ToSkia(frame.ClearColor));
        var frameBatches = frame.Batches;
        var firstUnlit = lit ? frame.Lighting.FirstUnlitBatch(frameBatches) : -1;
        for (var i = 0; i < frameBatches.Length; i++)
        {
            if (i == firstUnlit)
                CompositeLighting(canvas, viewMatrix, size, ref space, ref drawCalls);

            ref readonly var batch = ref frameBatches[i];
            batches++;
            var texture = _textures.Find(batch.Texture.Id);
            if (texture is null)
                continue;

            if (batch.Space != space)
            {
                space = batch.Space;
                canvas.SetMatrix(space == RenderSpace.World ? worldMatrix : screenMatrix);
            }

            var material = batch.Material;
            var shader = material.Shader is { } source ? _materialShaders.Get(material, source, batch.Texture.Id, texture, _frameIndex) : 0;
            SkiaNative.sk_paint_set_shader(paintHandle, shader != 0 ? shader : texture.Shader.Handle);
            _paint.BlendMode = ToSkia(material.Blend);

            if (batch.IsMesh)
            {
                SkiaNative.sk_canvas_draw_vertices(canvasHandle, _meshes.Get(batch, _frameIndex), SKBlendMode.Modulate, paintHandle);
                meshInstances += batch.MeshInstances!.Length;
            }
            else
            {
                if (batch.InstanceCount == 0)
                    continue;
                var vertices = _quads.Create(instances.Slice(batch.FirstInstance, batch.InstanceCount));
                SkiaNative.sk_canvas_draw_vertices(canvasHandle, vertices, SKBlendMode.Modulate, paintHandle);
                SkiaNative.sk_vertices_unref(vertices);
                sprites += batch.InstanceCount;
            }

            drawCalls++;
        }

        if (firstUnlit == frameBatches.Length)
            CompositeLighting(canvas, viewMatrix, size, ref space, ref drawCalls);

        SkiaNative.sk_paint_set_shader(paintHandle, 0);
        canvas.Restore();
        return new RenderStats(drawCalls, batches, sprites, meshInstances, 0);
    }

    private void CompositeLighting(SKCanvas canvas, SKMatrix viewMatrix, SKSizeI size, ref RenderSpace space, ref int drawCalls)
    {
        _lighting.Composite(canvas, viewMatrix, size);
        space = (RenderSpace)byte.MaxValue;
        drawCalls++;
    }

    private void UpdateInfo(GRRecordingContext? context)
    {
        var device = context is null ? "CPU" : "GPU";
        if (_info.Device != device)
            _info = new RendererInfo(Backend, device, context?.MaxTextureSize ?? CpuMaxTextureSize, SupportsCustomShaders: true);
    }

    private void Report(in RenderStats stats, int uploads)
    {
        if (_profiler is null)
            return;
        _profiler.Increment(DrawCallsCounter, stats.DrawCalls);
        _profiler.Increment(BatchesCounter, stats.Batches);
        _profiler.Increment(SpritesCounter, stats.Sprites);
        _profiler.Increment(MeshInstancesCounter, stats.MeshInstances);
        _profiler.Increment(TextureUploadsCounter, uploads);
    }

    internal static SKMatrix ToSkia(in Matrix3x2 m) => new(m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1);

    /// <summary>The frame's view rectangle within a target, or the whole target when the frame was laid out for another size.</summary>
    private static SKRectI ViewArea(in ViewLayout layout, SKSizeI size)
    {
        var rect = layout.ViewRect;
        var whole = SKRectI.Create(size);
        var view = SKRectI.Intersect(whole, SKRectI.Create((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height));
        return view.IsEmpty ? whole : view;
    }

    private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B, color.A);

    private static SKBlendMode ToSkia(BlendMode blend) => blend switch
    {
        BlendMode.Additive => SKBlendMode.Plus,
        BlendMode.Multiply => SKBlendMode.Multiply,
        BlendMode.Opaque => SKBlendMode.Src,
        _ => SKBlendMode.SrcOver
    };
}
