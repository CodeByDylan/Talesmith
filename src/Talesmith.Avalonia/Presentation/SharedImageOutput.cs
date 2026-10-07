using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Talesmith.Diagnostics;
using Talesmith.Rendering;
using Talesmith.Rendering.Vulkan;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Renders frames into Vulkan images that Avalonia's compositor imports, so frames reach the window without passing through the CPU.</summary>
/// <remarks>
/// <para>Three images rotate, so the GPU draws a frame while the compositor still takes earlier ones. Each frame waits on the GPU for the
/// compositor to release its image, and the compositor waits on the GPU for the frame to finish, so neither the render thread nor the UI
/// thread blocks on the GPU. The render thread only waits when the compositor still holds the image it is about to reuse.</para>
/// </remarks>
internal sealed class SharedImageOutput : IVulkanOutput
{
    private const string ImageHandleType = KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor;
    private const string SemaphoreHandleType = KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaquePosixFileDescriptor;
    private const int WaitStepMilliseconds = 50;

    private static readonly ProfilerMarker HandOverMarker = ProfilerMarker.Get("Render/Wait for compositor", "Rendering");

    private readonly VulkanRenderer _renderer;
    private readonly ICompositionGpuInterop _interop;
    private readonly bool _dedicatedAllocation;
    private Slot[] _slots = [];
    private CancellationToken _stopping;
    private int _next;
    private volatile CompositionDrawingSurface? _surface;
    private CompositionSurfaceVisual? _visual;
    private GameView? _view;
    private volatile bool _disposed;

    /// <param name="dedicatedAllocation">Whether the compositor imports images as dedicated allocations, as Avalonia's Vulkan compositor does.</param>
    public SharedImageOutput(VulkanRenderer renderer, ICompositionGpuInterop interop, bool dedicatedAllocation)
    {
        _renderer = renderer;
        _interop = interop;
        _dedicatedAllocation = dedicatedAllocation;
    }

    public void Attach(GameView view)
    {
        if (ElementComposition.GetElementVisual(view)?.Compositor is not { } compositor)
            return;
        _view = view;
        _visual = compositor.CreateSurfaceVisual();
        _visual.Surface = _surface = compositor.CreateDrawingSurface();
        _visual.Size = new Vector(view.Bounds.Width, view.Bounds.Height);
        ElementComposition.SetElementChildVisual(view, _visual);
        view.PropertyChanged += OnViewPropertyChanged;
    }

    public void Detach(GameView view)
    {
        view.PropertyChanged -= OnViewPropertyChanged;
        ElementComposition.SetElementChildVisual(view, null);
        _visual = null;
        _view = null;
        _surface = null;
    }

    public void Present(RenderFrame frame, int width, int height, Profiler profiler, CancellationToken stopping)
    {
        if (_surface is not { } surface)
            return;

        _stopping = stopping;
        EnsureSlots(width, height);
        var slot = _slots[_next];
        _next = (_next + 1) % _slots.Length;
        using (profiler.Measure(HandOverMarker))
            WaitFor(slot.Update);

        _renderer.RenderToSharedImage(frame, slot.Image, waitForRelease: slot.ReleaseSignaled);
        slot.ReleaseSignaled = false;

        slot.Update = slot.HandOver(surface);
    }

    public void Render(DrawingContext context, Rect bounds)
    {
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var slots = _slots;
        _slots = [];
        foreach (var slot in slots)
            slot.Image.Dispose();
        Dispatcher.UIThread.Post(() =>
        {
            if (_view is { } view)
                Detach(view);
            foreach (var slot in slots)
                _ = slot.DisposeImportsAsync();
        });
    }

    private void OnViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty && _visual is { } visual && sender is GameView view)
            visual.Size = new Vector(view.Bounds.Width, view.Bounds.Height);
    }

    private void EnsureSlots(int width, int height)
    {
        if (_slots.Length > 0 && _slots[0].Image.Width == width && _slots[0].Image.Height == height)
            return;

        var old = _slots;
        foreach (var slot in old)
            WaitFor(slot.Update);
        foreach (var slot in old)
            slot.Image.Dispose();
        if (old.Length > 0)
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var slot in old)
                    _ = slot.DisposeImportsAsync();
            });

        _slots = [CreateSlot(width, height), CreateSlot(width, height), CreateSlot(width, height)];
        _next = 0;
    }

    private Slot CreateSlot(int width, int height)
    {
        var image = _renderer.CreateSharedImage(width, height, _dedicatedAllocation);
        try
        {
            var memory = image.ExportMemory();
            var ready = image.ExportReadySemaphore();
            var released = image.ExportReleasedSemaphore();
            var properties = new PlatformGraphicsExternalImageProperties
            {
                Width = width,
                Height = height,
                Format = PlatformGraphicsExternalImageFormat.R8G8B8A8UNorm,
                MemorySize = image.MemorySize,
                MemoryOffset = 0,
                TopLeftOrigin = true
            };

            var imports = new TaskCompletionSource<Slot>(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    imports.SetResult(new Slot(image,
                        _interop.ImportImage(new PlatformHandle(memory, ImageHandleType), properties),
                        _interop.ImportSemaphore(new PlatformHandle(ready, SemaphoreHandleType)),
                        _interop.ImportSemaphore(new PlatformHandle(released, SemaphoreHandleType))));
                }
                catch (Exception ex)
                {
                    imports.SetException(ex);
                }
            });

            var slot = WaitFor(imports.Task);
            WaitFor(slot.ImportedImage.ImportCompleted);
            WaitFor(slot.Ready.ImportCompleted);
            WaitFor(slot.Released.ImportCompleted);
            return slot;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Waits for a task without blocking shutdown, which waits for the render thread on the UI thread.</summary>
    private void WaitFor(Task task)
    {
        while (!task.IsCompleted)
        {
            if (_disposed || _stopping.IsCancellationRequested)
                throw new OperationCanceledException("The output is shutting down.");
            ((IAsyncResult)task).AsyncWaitHandle.WaitOne(WaitStepMilliseconds);
        }

        task.GetAwaiter().GetResult();
    }

    private T WaitFor<T>(Task<T> task)
    {
        WaitFor((Task)task);
        return task.Result;
    }

    /// <summary>A shared image, its imports into the compositor and the state of handing it over.</summary>
    private sealed class Slot(VulkanSharedImage image, ICompositionImportedGpuImage importedImage, ICompositionImportedGpuSemaphore ready,
        ICompositionImportedGpuSemaphore released)
    {
        public VulkanSharedImage Image { get; } = image;

        public ICompositionImportedGpuImage ImportedImage { get; } = importedImage;

        public ICompositionImportedGpuSemaphore Ready { get; } = ready;

        public ICompositionImportedGpuSemaphore Released { get; } = released;

        /// <summary>Completes when the compositor took the frame and queued the release signal.</summary>
        public Task Update { get; set; } = Task.CompletedTask;

        /// <summary>Whether the compositor queued a signal of <see cref="Released"/> that the next frame must wait for.</summary>
        public volatile bool ReleaseSignaled;

        /// <summary>Hands the rendered frame to the compositor on the UI thread.</summary>
        public Task HandOver(CompositionDrawingSurface surface)
        {
            var handedOver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    surface.UpdateWithSemaphoresAsync(ImportedImage, Ready, Released).ContinueWith(update =>
                    {
                        if (update.IsCompletedSuccessfully)
                        {
                            ReleaseSignaled = true;
                            handedOver.SetResult();
                        }
                        else
                        {
                            handedOver.SetException(update.Exception?.InnerException ?? new OperationCanceledException("The compositor did not take the frame."));
                        }
                    }, TaskScheduler.Default);
                }
                catch (Exception ex)
                {
                    handedOver.SetException(ex);
                }
            }, DispatcherPriority.Render);
            return handedOver.Task;
        }

        public async Task DisposeImportsAsync()
        {
            await ImportedImage.DisposeAsync();
            await Ready.DisposeAsync();
            await Released.DisposeAsync();
        }
    }
}
