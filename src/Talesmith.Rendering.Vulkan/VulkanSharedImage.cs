using Talesmith.Rendering.Vulkan.Internal;

namespace Talesmith.Rendering.Vulkan;

/// <summary>An image the renderer draws finished frames into, for another graphics API in the same process to display without reading it back.</summary>
/// <remarks>
/// <para>The consumer imports the memory (RGBA8, optimal tiling, not a dedicated allocation) and both semaphores once, as POSIX file
/// descriptors whose ownership passes to it. After each <see cref="VulkanRenderer.RenderToSharedImage"/>, it waits on the ready semaphore,
/// reads the image in transfer-source layout and signals the released semaphore.</para>
/// <para>Create, render to and dispose shared images on the render thread. Disposing releases the GPU resources once frames using them complete.</para>
/// </remarks>
public sealed class VulkanSharedImage : IDisposable
{
    private readonly Action<VulkanSharedImage> _release;
    private bool _disposed;

    internal VulkanSharedImage(SharedImageResources resources, Action<VulkanSharedImage> release)
    {
        Resources = resources;
        _release = release;
    }

    public int Width => Resources.Width;

    public int Height => Resources.Height;

    /// <summary>The size of the image's memory allocation, which the consumer needs to import it.</summary>
    public ulong MemorySize => Resources.MemorySize;

    internal SharedImageResources Resources { get; }

    /// <summary>The last frame rendered into the image, or -1.</summary>
    internal long LastFrame { get; set; } = -1;

    /// <summary>Exports the image memory as a new file descriptor.</summary>
    public int ExportMemory() => Resources.ExportMemory();

    /// <summary>Exports the semaphore the renderer signals once a frame is in the image.</summary>
    public int ExportReadySemaphore() => Resources.ExportSemaphore(Resources.Ready);

    /// <summary>Exports the semaphore the consumer signals once it no longer reads the image.</summary>
    public int ExportReleasedSemaphore() => Resources.ExportSemaphore(Resources.Released);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _release(this);
    }
}
