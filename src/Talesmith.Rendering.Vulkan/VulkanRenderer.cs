using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Diagnostics;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering.Vulkan.Internal;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan;

/// <summary>Renders frames with Vulkan into offscreen images: instanced sprites, cached meshes, 2D lighting, custom SPIR-V materials and post effects.</summary>
/// <remarks>
/// <para>Create one with <see cref="Create"/>, which throws <see cref="VulkanUnavailableException"/> when no device is usable.</para>
/// <para>Texture methods may be called from any thread. Rendering belongs to one render thread. <see cref="Dispose"/> may be called from any
/// thread: it waits for a frame being rendered and releases the shared images that are still alive. <see cref="RenderAndReadBack"/> keeps
/// the GPU busy without stalling the CPU by returning the newest completed frame; <see cref="RenderToImage"/> waits for the frame it
/// renders.</para>
/// </remarks>
public sealed class VulkanRenderer : IRenderer, IOffscreenRenderer
{
    private static readonly ProfilerMarker WaitMarker = ProfilerMarker.Get("Render/Vulkan/Wait", "Rendering");
    private static readonly ProfilerMarker RecordMarker = ProfilerMarker.Get("Render/Vulkan/Record", "Rendering");
    private static readonly ProfilerMarker SubmitMarker = ProfilerMarker.Get("Render/Vulkan/Submit", "Rendering");
    private static readonly ProfilerMarker ReadbackMarker = ProfilerMarker.Get("Render/Vulkan/Readback", "Rendering");
    private static readonly ProfilerCounter DrawCallsCounter = ProfilerCounter.Get("Draw calls", CounterKind.PerFrame, "calls");
    private static readonly ProfilerCounter BatchesCounter = ProfilerCounter.Get("Batches", CounterKind.PerFrame, "batches");
    private static readonly ProfilerCounter SpritesCounter = ProfilerCounter.Get("Sprites", CounterKind.PerFrame, "sprites");
    private static readonly ProfilerCounter MeshInstancesCounter = ProfilerCounter.Get("Mesh instances", CounterKind.PerFrame, "instances");
    private static readonly ProfilerCounter TextureUploadsCounter = ProfilerCounter.Get("Texture uploads", CounterKind.PerFrame, "uploads");
    private static readonly ProfilerCounter GpuFrameCounter = ProfilerCounter.Get("GPU frame (ms)", CounterKind.Gauge, "ms");

    private readonly VulkanDevice _device;
    private readonly Profiler _profiler;
    private readonly DeferredReleaser _releaser = new();
    private readonly DescriptorAllocator _descriptors;
    private readonly Samplers _samplers;
    private readonly ShaderLibrary _shaders;
    private readonly PipelineLibrary _pipelines;
    private readonly TextureStore _textures;
    private readonly MeshCache _meshes;
    private readonly LightMapRecorder _lighting;
    private readonly SceneRecorder _recorder;
    private readonly FrameResources[] _frames;
    private readonly Action<VulkanSharedImage> _releaseSharedImage;
    private readonly Lock _gate = new();
    private readonly HashSet<VulkanSharedImage> _sharedImages = [];
    private RenderTargets? _targets;
    private ReadbackRing? _readbacks;
    private long _lastFrame = -1;
    private long _completedFrame = -1;
    private bool _disposed;

    private VulkanRenderer(VulkanDevice device, VulkanRendererOptions options, ILogger logger)
    {
        _device = device;
        _profiler = options.Profiler ?? new Profiler("Vulkan") { Enabled = false };
        _descriptors = new DescriptorAllocator(device);
        _samplers = new Samplers(device);
        _shaders = new ShaderLibrary(device, logger);
        _pipelines = new PipelineLibrary(device, _shaders, logger);
        // Builds the default pipeline now so a device that cannot run it fails in Create, where callers fall back.
        _pipelines.GetSprite(BlendMode.Alpha, null);
        _textures = new TextureStore(device, _descriptors, _pipelines.TextureLayout, _samplers, _releaser);
        _meshes = new MeshCache(device, _releaser);
        WhiteTexture = _textures.Create(ImageData.Solid(1, 1, Color.White), new TextureOptions(TextureFilter.Nearest, "White"));
        _lighting = new LightMapRecorder(device, _descriptors, _pipelines, _textures, _samplers, _releaser, WhiteTexture);
        _recorder = new SceneRecorder(device, _pipelines, _textures, _meshes, _lighting);
        var measureGpuTime = options.Profiler is not null && device.SupportsTimestamps;
        _releaseSharedImage = ReleaseSharedImage;
        _frames = new FrameResources[options.FramesInFlight];
        for (var i = 0; i < _frames.Length; i++)
            _frames[i] = new FrameResources(device, _descriptors, _pipelines.EffectLayout, _releaser, measureGpuTime);
        Info = new RendererInfo("Vulkan", device.Name, (int)device.Limits.maxImageDimension2D, SupportsCustomShaders: true);
    }

    public RendererInfo Info { get; }

    /// <summary>The UUID of the device in use, or null when the driver does not report one.</summary>
    public byte[]? DeviceUuid => _device.DeviceUuid?.ToArray();

    /// <summary>Whether <see cref="CreateSharedImage"/> is available: frames can be handed to another graphics API on the GPU.</summary>
    public bool SupportsSharedImages => _device.SupportsSharedImages;

    /// <summary>Why <see cref="SupportsSharedImages"/> is false, for logs.</summary>
    public string? SharedImagesUnsupportedReason => _device.SharedImagesUnsupportedReason;

    /// <summary>Whether shared images must be created with a dedicated allocation, so importers must import them that way.</summary>
    public bool SharedImagesRequireDedicatedAllocation => _device.SharedImagesRequireDedicatedAllocation;

    public Texture WhiteTexture { get; }

    /// <summary>Creates a renderer on the best available device, or the one the options ask for.</summary>
    /// <exception cref="VulkanUnavailableException">Vulkan is not installed, no device matches, or the device cannot be initialized.</exception>
    public static VulkanRenderer Create(VulkanRendererOptions? options = null)
    {
        options ??= new VulkanRendererOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.FramesInFlight, 1, nameof(options));
        var logger = options.Logger ?? NullLogger.Instance;
        VulkanDevice device;
        try
        {
            device = VulkanDevice.Create(options, logger);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or VkException)
        {
            throw new VulkanUnavailableException($"Vulkan could not be initialized: {exception.Message}", exception);
        }

        try
        {
            return new VulkanRenderer(device, options, logger);
        }
        catch (Exception exception) when (exception is VkException or InvalidOperationException)
        {
            device.Dispose();
            throw new VulkanUnavailableException($"The Vulkan device {device.Name} could not be set up: {exception.Message}", exception);
        }
    }

    public Texture CreateTexture(ImageData image, TextureOptions options = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _textures.Create(image, options);
    }

    public void UpdateTexture(Texture texture, ImageData image)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _textures.Update(texture, image);
    }

    public void DestroyTexture(Texture texture)
    {
        if (!_disposed)
            _textures.Destroy(texture);
    }

    /// <summary>Renders a frame and returns the pixels of the newest frame the GPU has completed, usually the previous one.</summary>
    /// <remarks>Only the very first frame at a new size waits for the GPU. The pixels stay valid until the next render call.</remarks>
    public ReadbackImage RenderAndReadBack(RenderFrame frame, int width, int height)
    {
        using var scope = _gate.EnterScope();
        var submitted = Submit(frame, width, height, null, false);
        using (_profiler.Measure(ReadbackMarker))
        {
            var shown = NewestCompletedFrame(submitted);
            var buffer = _readbacks!.Find(shown)!;
            buffer.InvalidateForRead();
            unsafe
            {
                var pixels = new ReadOnlySpan<byte>(buffer.Mapped, _readbacks.Width * _readbacks.Height * 4);
                return new ReadbackImage(_readbacks.Width, _readbacks.Height, pixels);
            }
        }
    }

    /// <summary>Creates an image to render frames into for another graphics API; call it on the render thread.</summary>
    /// <param name="dedicatedAllocation">Gives the image its own memory allocation, for importers that import it as such.</param>
    /// <exception cref="NotSupportedException"><see cref="SupportsSharedImages"/> is false, or the driver requires a dedicated allocation and
    /// <paramref name="dedicatedAllocation"/> is false.</exception>
    public VulkanSharedImage CreateSharedImage(int width, int height, bool dedicatedAllocation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_device.SupportsSharedImages)
            throw new NotSupportedException($"Shared images are not supported: {_device.SharedImagesUnsupportedReason}.");
        if (_device.SharedImagesRequireDedicatedAllocation && !dedicatedAllocation)
            throw new NotSupportedException("This driver shares images only as dedicated allocations.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var image = new VulkanSharedImage(new SharedImageResources(_device, width, height, dedicatedAllocation), _releaseSharedImage);
            _sharedImages.Add(image);
            return image;
        }
    }

    /// <summary>Renders a frame into a shared image of the same size, without waiting for the GPU.</summary>
    /// <param name="waitForRelease">Whether the consumer has signaled the released semaphore since the image was last rendered; the GPU then
    /// waits for it before drawing into the image again.</param>
    public void RenderToSharedImage(RenderFrame frame, VulkanSharedImage target, bool waitForRelease)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
            target.LastFrame = Submit(frame, target.Width, target.Height, target, waitForRelease);
    }

    /// <summary>Renders and reads back like <see cref="RenderAndReadBack"/>, discarding the pixels.</summary>
    public void RenderOffscreen(RenderFrame frame, int width, int height) => RenderAndReadBack(frame, width, height);

    public ImageData RenderToImage(RenderFrame frame, int width, int height)
    {
        using var scope = _gate.EnterScope();
        var submitted = Submit(frame, width, height, null, false);
        using (_profiler.Measure(ReadbackMarker))
        {
            _frames[submitted % _frames.Length].WaitForCompletion();
            var buffer = _readbacks!.Find(submitted)!;
            buffer.InvalidateForRead();
            var pixels = new byte[width * height * 4];
            unsafe
            {
                PixelFormats.BgraToRgba(new ReadOnlySpan<byte>(buffer.Mapped, pixels.Length), pixels);
            }

            return new ImageData(width, height, pixels);
        }
    }

    public void Dispose()
    {
        using var scope = _gate.EnterScope();
        if (_disposed)
            return;
        _disposed = true;
        _device.WaitIdle();
        foreach (var image in _sharedImages)
            image.Resources.Dispose();
        _sharedImages.Clear();
        foreach (var frame in _frames)
            frame.Dispose();
        _lighting.Dispose();
        _targets?.Dispose();
        _readbacks?.Dispose();
        _meshes.Dispose();
        _textures.Dispose();
        _releaser.Dispose();
        _pipelines.Dispose();
        _shaders.Dispose();
        _samplers.Dispose();
        _descriptors.Dispose();
        _device.Dispose();
    }

    private long Submit(RenderFrame frame, int width, int height, VulkanSharedImage? shared, bool waitForRelease)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, Info.MaxTextureSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, Info.MaxTextureSize);
        frame.Finish();

        var number = _lastFrame + 1;
        var resources = _frames[number % _frames.Length];
        using (_profiler.Measure(WaitMarker))
            resources.WaitForCompletion();
        if (resources.TryReadGpuMilliseconds(out var gpuMilliseconds))
            _profiler.Set(GpuFrameCounter, gpuMilliseconds);
        _completedFrame = Math.Max(_completedFrame, resources.SubmittedFrame);
        _releaser.DisposeCompleted(_completedFrame);
        var placement = ViewPlacement.Of(frame, width, height);
        EnsureTargets(placement, readback: shared is null);

        RenderStats stats;
        int uploads;
        using (_profiler.Measure(RecordMarker))
        {
            resources.BeginRecording();
            uploads = _textures.ApplyPending(resources.Commands, resources.Staging, number);
            var finished = _recorder.Record(resources, frame, _targets!, number, out stats);
            if (shared is null)
            {
                var readback = _readbacks!.Claim(number, placement, out var fillBorders);
                _recorder.CopyToReadback(resources.Commands, _targets!.Image(finished), readback, placement, fillBorders);
            }
            else
            {
                _recorder.CopyToShared(resources.Commands, _targets!.Image(finished), shared.Resources, placement, fromExternal: shared.LastFrame >= 0);
            }
            resources.EndRecording();
        }

        using (_profiler.Measure(SubmitMarker))
            SubmitCommands(resources, shared?.Resources, waitForRelease);
        resources.SubmittedFrame = number;
        _lastFrame = number;
        _meshes.EvictUnused(number);

        _profiler.Increment(DrawCallsCounter, stats.DrawCalls);
        _profiler.Increment(BatchesCounter, stats.Batches);
        _profiler.Increment(SpritesCounter, stats.Sprites);
        _profiler.Increment(MeshInstancesCounter, stats.MeshInstances);
        _profiler.Increment(TextureUploadsCounter, uploads);
        return number;
    }

    private unsafe void SubmitCommands(FrameResources resources, SharedImageResources? shared, bool waitForRelease)
    {
        var commands = resources.Commands;
        var released = shared?.Released ?? VkSemaphore.Null;
        var ready = shared?.Ready ?? VkSemaphore.Null;
        var waitStage = VkPipelineStageFlags.Transfer;
        var submitInfo = new VkSubmitInfo
        {
            commandBufferCount = 1,
            pCommandBuffers = &commands,
            waitSemaphoreCount = shared is not null && waitForRelease ? 1u : 0u,
            pWaitSemaphores = &released,
            pWaitDstStageMask = &waitStage,
            signalSemaphoreCount = shared is not null ? 1u : 0u,
            pSignalSemaphores = &ready
        };
        _device.Api.vkQueueSubmit(_device.Queue, 1, &submitInfo, resources.Fence).CheckResult();
    }

    /// <summary>Sizes the scene targets to the view and the readback buffers to the whole output.</summary>
    private void EnsureTargets(in ViewPlacement placement, bool readback)
    {
        if (_targets is null || _targets.Width != placement.Width || _targets.Height != placement.Height)
        {
            WaitForAllFrames();
            _targets?.Dispose();
            _targets = new RenderTargets(_device, _descriptors, _pipelines, _samplers.Linear, placement.Width, placement.Height);
        }

        if (_readbacks is not null && (_readbacks.Width != placement.OutputWidth || _readbacks.Height != placement.OutputHeight))
        {
            WaitForAllFrames();
            _readbacks.Dispose();
            _readbacks = null;
        }

        if (readback && _readbacks is null)
        {
            WaitForAllFrames();
            _readbacks = new ReadbackRing(_device, _frames.Length + 1, placement.OutputWidth, placement.OutputHeight);
        }
    }

    private void WaitForAllFrames()
    {
        foreach (var frame in _frames)
            frame.WaitForCompletion();
        _completedFrame = _lastFrame;
        _releaser.DisposeCompleted(_completedFrame);
    }

    private void ReleaseSharedImage(VulkanSharedImage image)
    {
        lock (_gate)
        {
            if (!_sharedImages.Remove(image))
                return;
            if (image.LastFrame < 0)
                image.Resources.Dispose();
            else
                _releaser.Release(image.Resources, image.LastFrame);
        }
    }

    /// <summary>Finds the newest frame at the current size whose pixels are ready, waiting only when none is.</summary>
    private long NewestCompletedFrame(long submitted)
    {
        var oldest = Math.Max(0, submitted - _frames.Length);
        for (var frame = submitted - 1; frame >= oldest; frame--)
        {
            if (_readbacks!.Find(frame) is not null && IsComplete(frame))
                return frame;
        }

        var waitFor = submitted;
        for (var frame = oldest; frame < submitted; frame++)
        {
            if (_readbacks!.Find(frame) is not null)
            {
                waitFor = frame;
                break;
            }
        }

        _frames[waitFor % _frames.Length].WaitForCompletion();
        return waitFor;
    }

    private bool IsComplete(long frame)
    {
        var resources = _frames[frame % _frames.Length];
        return frame <= _completedFrame || resources.SubmittedFrame != frame || resources.IsComplete;
    }
}
