using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Diagnostics;
using Talesmith.Events;
using Talesmith.Imaging;
using Talesmith.Rendering;
using Talesmith.Rendering.Vulkan;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Renders with Vulkan on a dedicated thread and hands frames to the window as shared GPU images when the window can import them.</summary>
public sealed class VulkanBackend(VulkanRenderer renderer, WindowGraphics graphics, ILogger logger) : IRenderBackend
{
    public IRenderer Renderer => renderer;

    /// <summary>Whether frames reach the window as shared GPU images rather than by reading them back.</summary>
    public bool SharesImages { get; } = renderer.SupportsSharedImages && graphics.CanImportVulkanImages(out _);

    public IGamePresenter CreatePresenter(Game game) => new VulkanPresenter(renderer, game, SharesImages ? graphics : WindowGraphics.None, logger);

    public void Dispose() => renderer.Dispose();
}

/// <summary>Renders every published frame on its own thread and shows it through a <see cref="IVulkanOutput"/>.</summary>
/// <remarks>
/// Frames are shared with the compositor when possible. If sharing fails while the game runs, the presenter logs why and continues by
/// reading frames back, so the game keeps showing.
/// </remarks>
public sealed partial class VulkanPresenter : IGamePresenter
{
    private static readonly ProfilerMarker FrameMarker = ProfilerMarker.Get("Render/Frame", "Rendering");

    private readonly VulkanRenderer _renderer;
    private readonly Game _game;
    private readonly ILogger _logger;
    private readonly Thread _thread;
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly CancellationTokenSource _stopping = new();
    private volatile IVulkanOutput _output;
    private volatile GameView? _view;
    private TaskCompletionSource<ImageData>? _capture;
    private volatile bool _disposed;

    public VulkanPresenter(VulkanRenderer renderer, Game game, WindowGraphics graphics, ILogger logger)
    {
        _renderer = renderer;
        _game = game;
        _logger = logger;
        _output = graphics.Interop is { } interop && graphics.CanImportVulkanImages(out _) && renderer.SupportsSharedImages
            ? new SharedImageOutput(renderer, interop, dedicatedAllocation: graphics.ImportsWithVulkan)
            : new ReadbackOutput(renderer);
        _thread = new Thread(RenderLoop) { Name = "Talesmith Vulkan render", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Whether frames currently reach the window as shared GPU images.</summary>
    public bool SharesImages => _output is SharedImageOutput;

    public void Attach(GameView view)
    {
        _view = view;
        _output.Attach(view);
    }

    public void Detach(GameView view)
    {
        _output.Detach(view);
        _view = null;
    }

    public void OnFramePublished(GameView view) => _frameReady.Set();

    public void Render(DrawingContext context, Rect bounds, PixelSize pixelSize) => _output.Render(context, bounds);

    public Task<ImageData> CaptureAsync()
    {
        var capture = new TaskCompletionSource<ImageData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Interlocked.CompareExchange(ref _capture, capture, null) ?? capture;
        _frameReady.Set();
        return pending.Task;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stopping.Cancel();
        _frameReady.Set();
        _thread.Join();
        _frameReady.Dispose();
        _stopping.Dispose();
        _output.Dispose();
        Interlocked.Exchange(ref _capture, null)?.TrySetCanceled();
    }

    private void RenderLoop()
    {
        while (true)
        {
            _frameReady.WaitOne();
            if (_disposed)
                return;
            if (_game.Frames.BeginRead() is not { } frame)
                continue;

            var profiler = _game.Profilers.Render;
            profiler.BeginFrame();
            try
            {
                using (profiler.Measure(FrameMarker))
                    RenderFrame(frame, profiler);
            }
            catch (OperationCanceledException) when (_disposed)
            {
                return;
            }
            catch (ObjectDisposedException ex) when (IsRendererDisposed(ex))
            {
                return;
            }
            catch (Exception ex)
            {
                LogRenderFailed(_logger, ex);
            }
            finally
            {
                _game.Frames.EndRead(frame);
                profiler.EndFrame();
            }
        }
    }

    private void RenderFrame(RenderFrame frame, Profiler profiler)
    {
        var width = Math.Max(1, (int)frame.View.TargetSize.X);
        var height = Math.Max(1, (int)frame.View.TargetSize.Y);
        try
        {
            _output.Present(frame, width, height, profiler, _stopping.Token);
        }
        catch (Exception ex) when (_output is SharedImageOutput && !_disposed && !IsRendererDisposed(ex))
        {
            LogSharingFailed(_logger, ex);
            FallBackToReadback();
            _output.Present(frame, width, height, profiler, _stopping.Token);
        }

        if (Interlocked.Exchange(ref _capture, null) is { } capture)
            Capture(capture, frame, width, height);
    }

    private void FallBackToReadback()
    {
        var failed = _output;
        var readback = new ReadbackOutput(_renderer);
        _output = readback;
        failed.Dispose();
        _game.Services.GetRequiredService<IEventBus>().Enqueue(
            new RenderBackendChanged(_renderer.Info, _renderer.Info, "Sharing frames with the window failed, so Vulkan reads frames back through the CPU"));
        Dispatcher.UIThread.Post(() =>
        {
            if (_view is { } view && ReferenceEquals(_output, readback))
                readback.Attach(view);
        });
    }

    private void Capture(TaskCompletionSource<ImageData> capture, RenderFrame frame, int width, int height)
    {
        try
        {
            capture.TrySetResult(_renderer.RenderToImage(frame, width, height));
        }
        catch (Exception ex)
        {
            capture.TrySetException(ex);
        }
    }

    private static bool IsRendererDisposed(Exception exception) =>
        exception is ObjectDisposedException { ObjectName: var name } && name == typeof(VulkanRenderer).FullName;

    [LoggerMessage(Level = LogLevel.Error, Message = "Rendering a frame with Vulkan failed")]
    private static partial void LogRenderFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sharing frames with the window failed, so frames are copied to it from now on")]
    private static partial void LogSharingFailed(ILogger logger, Exception exception);
}

/// <summary>How a <see cref="VulkanPresenter"/> gets rendered frames into the window.</summary>
internal interface IVulkanOutput : IDisposable
{
    /// <summary>Called on the UI thread when the view enters the visual tree.</summary>
    void Attach(GameView view);

    /// <summary>Called on the UI thread when the view leaves the visual tree.</summary>
    void Detach(GameView view);

    /// <summary>Renders a frame and hands it to the window; called on the render thread.</summary>
    /// <param name="stopping">Canceled when the presenter shuts down; waits for the UI thread must end then.</param>
    void Present(RenderFrame frame, int width, int height, Profiler profiler, CancellationToken stopping);

    /// <summary>Draws the newest frame while the view renders; called on the UI thread.</summary>
    void Render(DrawingContext context, Rect bounds);
}
