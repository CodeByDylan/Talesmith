using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>A frame's upload memory: a mapped buffer handed out front to back and reset when the frame's GPU work has completed.</summary>
internal sealed unsafe class StagingBuffer(VulkanDevice device, DeferredReleaser releaser) : IDisposable
{
    private const ulong MinimumSize = 1 << 20;

    private GpuBuffer? _buffer;
    private ulong _used;

    public void Reset() => _used = 0;

    /// <summary>Reserves <paramref name="size"/> bytes; copies recorded from earlier reservations stay valid if the buffer grows.</summary>
    public StagingSpan Allocate(ulong size, long frame)
    {
        var offset = (_used + 15) & ~15ul;
        if (_buffer is null || offset + size > _buffer.Size)
        {
            if (_buffer is not null)
                releaser.Release(_buffer, frame);
            var capacity = Math.Max(MinimumSize, _buffer is null ? size : Math.Max(_buffer.Size * 2, size));
            _buffer = new GpuBuffer(device, capacity, VkBufferUsageFlags.TransferSrc, BufferMemory.Upload);
            offset = 0;
        }

        _used = offset + size;
        return new StagingSpan(_buffer.Handle, offset, _buffer.Mapped + offset);
    }

    public void Dispose() => _buffer?.Dispose();
}

internal readonly unsafe struct StagingSpan(VkBuffer buffer, ulong offset, byte* pointer)
{
    public readonly VkBuffer Buffer = buffer;
    public readonly ulong Offset = offset;
    public readonly byte* Pointer = pointer;
}
