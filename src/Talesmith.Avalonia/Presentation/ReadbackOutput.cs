using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Talesmith.Diagnostics;
using Talesmith.Rendering;
using Talesmith.Rendering.Vulkan;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Reads each frame back from the GPU and shows it as a bitmap; works with any compositor, at the cost of copying every frame.</summary>
/// <remarks>
/// The render thread never waits for the UI and the UI never waits for the GPU: the newest read-back image replaces any image the UI has
/// not picked up yet.
/// </remarks>
internal sealed class ReadbackOutput : IVulkanOutput
{
    private static readonly ProfilerMarker StageMarker = ProfilerMarker.Get("Render/Stage pixels", "Rendering");

    private readonly VulkanRenderer _renderer;
    private readonly Lock _stagingLock = new();
    private readonly Action _upload;
    private PixelBuffer _back = new();
    private PixelBuffer _ready = new();
    private bool _hasReady;
    private int _uploadPending;
    private WriteableBitmap? _bitmap;
    private volatile GameView? _view;
    private volatile bool _disposed;

    public ReadbackOutput(VulkanRenderer renderer)
    {
        _renderer = renderer;
        _upload = Upload;
    }

    public void Attach(GameView view) => _view = view;

    public void Detach(GameView view) => _view = null;

    public void Present(RenderFrame frame, int width, int height, Profiler profiler, CancellationToken stopping)
    {
        var image = _renderer.RenderAndReadBack(frame, width, height);
        using (profiler.Measure(StageMarker))
        {
            _back.CopyFrom(image.Pixels, image.Width, image.Height);
            lock (_stagingLock)
            {
                (_back, _ready) = (_ready, _back);
                _hasReady = true;
            }
        }

        if (Interlocked.Exchange(ref _uploadPending, 1) == 0)
            Dispatcher.UIThread.Post(_upload, DispatcherPriority.Render);
    }

    public void Render(DrawingContext context, Rect bounds)
    {
        if (_bitmap is { } bitmap)
            context.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), bounds);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Dispatcher.UIThread.Post(() =>
        {
            _bitmap?.Dispose();
            _bitmap = null;
        });
    }

    private void Upload()
    {
        Volatile.Write(ref _uploadPending, 0);
        if (_disposed)
            return;
        lock (_stagingLock)
        {
            if (!_hasReady)
                return;
            _hasReady = false;
            var size = new PixelSize(_ready.Width, _ready.Height);
            if (_bitmap is null || _bitmap.PixelSize != size)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }

            using var target = _bitmap.Lock();
            _ready.CopyTo(target);
        }

        _view?.InvalidateVisual();
    }

    private sealed class PixelBuffer
    {
        private byte[] _pixels = [];

        public int Width { get; private set; }

        public int Height { get; private set; }

        public void CopyFrom(ReadOnlySpan<byte> pixels, int width, int height)
        {
            if (_pixels.Length < pixels.Length)
                _pixels = new byte[pixels.Length];
            pixels.CopyTo(_pixels);
            Width = width;
            Height = height;
        }

        public void CopyTo(ILockedFramebuffer target)
        {
            var rowBytes = Width * 4;
            for (var y = 0; y < Height; y++)
                Marshal.Copy(_pixels, y * rowBytes, target.Address + y * target.RowBytes, rowBytes);
        }
    }
}
