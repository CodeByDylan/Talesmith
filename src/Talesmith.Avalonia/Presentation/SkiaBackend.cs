using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using Talesmith.Imaging;
using Talesmith.Rendering;
using Talesmith.Rendering.Skia;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Renders with Skia directly onto Avalonia's own canvas, on Avalonia's render thread.</summary>
public sealed class SkiaBackend(SkiaRenderer renderer) : IRenderBackend
{
    private readonly SkiaRendererState _state = new();

    public IRenderer Renderer => renderer;

    public IGamePresenter CreatePresenter(Game game) => new SkiaPresenter(renderer, game, _state);

    public void Dispose()
    {
        lock (SkiaRenderThread.Gate)
        {
            if (_state.Released)
                return;
            _state.Released = true;
            if (!_state.UsedGpu)
            {
                renderer.Dispose();
                return;
            }
        }

        SkiaRenderThread.Release(renderer);
    }
}

/// <summary>Whether a renderer drew with Avalonia's GPU context and whether its backend let go of it; guarded by <see cref="SkiaRenderThread.Gate"/>.</summary>
public sealed class SkiaRendererState
{
    public bool UsedGpu { get; set; }

    public bool Released { get; set; }
}

/// <summary>Draws each frame through a custom draw operation, so the game is composited with the UI without copying pixels.</summary>
public sealed class SkiaPresenter(SkiaRenderer renderer, Game game, SkiaRendererState? state = null) : IGamePresenter
{
    private readonly SkiaRendererState _state = state ?? new SkiaRendererState();
    private readonly FrameCache _cache = new();
    private TaskCompletionSource<ImageData>? _capture;
    private volatile GameView? _view;
    private Action? _invalidate;
    private int _invalidatePending;
    private volatile bool _disposed;

    public void Attach(GameView view) => _view = view;

    public void Detach(GameView view) => _view = null;

    public void OnFramePublished(GameView view)
    {
        if (Dispatcher.UIThread.CheckAccess())
            view.InvalidateVisual();
        else if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
            Dispatcher.UIThread.Post(_invalidate ??= Invalidate, DispatcherPriority.Render);
    }

    private void Invalidate()
    {
        Volatile.Write(ref _invalidatePending, 0);
        if (!_disposed)
            _view?.InvalidateVisual();
    }

    public void Render(DrawingContext context, Rect bounds, PixelSize pixelSize)
    {
        if (!_disposed && pixelSize.Width > 0 && pixelSize.Height > 0)
            context.Custom(new DrawOperation(this, bounds, pixelSize, SamplingOf(_view)));
    }

    public Task<ImageData> CaptureAsync()
    {
        var capture = new TaskCompletionSource<ImageData>(TaskCreationOptions.RunContinuationsAsynchronously);
        return (Interlocked.CompareExchange(ref _capture, capture, null) ?? capture).Task;
    }

    public void Dispose()
    {
        _disposed = true;
        Interlocked.Exchange(ref _capture, null)?.TrySetCanceled();
        SkiaRenderThread.Release(_cache);
    }

    /// <summary>Samples frames as Avalonia samples images where the view is: by the bitmap interpolation mode of the outermost visual that
    /// sets one.</summary>
    private static SKSamplingOptions SamplingOf(Visual? view)
    {
        var mode = BitmapInterpolationMode.Unspecified;
        for (var visual = view; visual is not null; visual = visual.GetVisualParent())
        {
            var own = RenderOptions.GetBitmapInterpolationMode(visual);
            if (own != BitmapInterpolationMode.Unspecified)
                mode = own;
        }

        return mode.ToSKSamplingOptions();
    }

    private void Draw(ImmediateDrawingContext context, Rect bounds, PixelSize pixelSize, SKSamplingOptions sampling)
    {
        lock (SkiaRenderThread.Gate)
        {
            if (!_disposed && !_state.Released)
                DrawLocked(context, bounds, pixelSize, sampling);
        }
    }

    private void DrawLocked(ImmediateDrawingContext context, Rect bounds, PixelSize pixelSize, SKSamplingOptions sampling)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } leaseFeature || game.Frames.BeginRead(out var isNew) is not { } frame)
            return;

        var profiler = game.Profilers.Render;
        var measured = false;
        try
        {
            using var lease = leaseFeature.Lease();
            SkiaRenderThread.Drain();
            _state.UsedGpu |= lease.GrContext is not null;
            var canvas = lease.SkCanvas;
            var size = new SKSizeI(pixelSize.Width, pixelSize.Height);
            var reuse = !isNew && _capture is null && _cache.Matches(size, lease.GrContext);
            if (!reuse)
            {
                profiler.BeginFrame();
                measured = true;
            }

            canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
                canvas.Scale((float)(bounds.Width / pixelSize.Width), (float)(bounds.Height / pixelSize.Height));
                if (!reuse && _cache.Prepare(size, lease.GrContext) is { } cached)
                {
                    renderer.Render(frame, cached, size);
                    _cache.Fill();
                }

                if (_cache.Matches(size, lease.GrContext))
                    _cache.Draw(canvas, sampling);
                else
                    renderer.Render(frame, canvas, size);
            }
            finally
            {
                canvas.Restore();
            }

            if (!reuse)
                CompleteCapture(frame, size);
        }
        catch (ObjectDisposedException)
        {
            // The session disposed the renderer while this view still had a frame to draw.
        }
        finally
        {
            game.Frames.EndRead(frame);
            if (measured)
                profiler.EndFrame();
        }
    }

    private void CompleteCapture(RenderFrame frame, SKSizeI size)
    {
        if (Interlocked.Exchange(ref _capture, null) is not { } capture)
            return;
        try
        {
            capture.TrySetResult(renderer.RenderToImage(frame, size.Width, size.Height));
        }
        catch (Exception ex)
        {
            capture.TrySetException(ex);
        }
    }

    /// <summary>The last rendered frame, so repainting an unchanged frame, such as when an overlay above the view changes, copies it instead of
    /// rendering the scene again.</summary>
    /// <remarks>Used on the render thread under <see cref="SkiaRenderThread.Gate"/>.</remarks>
    private sealed class FrameCache : IDisposable
    {
        private SKSurface? _surface;
        private SKSizeI _size;
        private GRContext? _context;
        private bool _filled;

        public bool Matches(SKSizeI size, GRContext? context) => _filled && size == _size && ReferenceEquals(context, _context);

        /// <summary>A cleared canvas to render the next frame on, or null when no surface of the size can be made.</summary>
        public SKCanvas? Prepare(SKSizeI size, GRContext? context)
        {
            _filled = false;
            if (_surface is null || size != _size || !ReferenceEquals(context, _context))
            {
                _surface?.Dispose();
                var info = new SKImageInfo(size.Width, size.Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul);
                _surface = context is not null ? SKSurface.Create(context, false, info) : SKSurface.Create(info);
                _size = size;
                _context = context;
            }

            if (_surface is null)
                return null;
            _surface.Canvas.Clear(SKColors.Transparent);
            return _surface.Canvas;
        }

        /// <summary>Marks the canvas from <see cref="Prepare"/> as holding a whole frame.</summary>
        public void Fill() => _filled = _surface is not null;

        public void Draw(SKCanvas canvas, SKSamplingOptions sampling)
        {
            if (_surface is null)
                return;
            using var frame = _surface.Snapshot();
            canvas.DrawImage(frame, 0, 0, sampling);
        }

        public void Dispose()
        {
            _surface?.Dispose();
            _surface = null;
            _filled = false;
        }
    }

    private sealed class DrawOperation(SkiaPresenter presenter, Rect bounds, PixelSize pixelSize, SKSamplingOptions sampling) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context) => presenter.Draw(context, bounds, pixelSize, sampling);

        // Every operation draws the newest frame, so none is equal to the previous one.
        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }
    }
}
