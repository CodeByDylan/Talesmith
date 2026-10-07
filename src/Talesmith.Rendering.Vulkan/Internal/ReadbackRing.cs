using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Mapped buffers that receive finished frames, one more than the frames in flight so the newest completed frame stays readable.</summary>
internal sealed class ReadbackRing : IDisposable
{
    private readonly GpuBuffer[] _buffers;
    private readonly long[] _frames;
    private readonly ViewPlacement[] _placements;

    public ReadbackRing(VulkanDevice device, int count, int width, int height)
    {
        Width = width;
        Height = height;
        _buffers = new GpuBuffer[count];
        _frames = new long[count];
        _placements = new ViewPlacement[count];
        for (var i = 0; i < count; i++)
        {
            _buffers[i] = new GpuBuffer(device, (ulong)width * (ulong)height * 4, VkBufferUsageFlags.TransferDst, BufferMemory.Readback);
            _frames[i] = -1;
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Claims the buffer for <paramref name="frame"/>, which overwrites the frame stored there before.</summary>
    /// <param name="fillBorders">Set when the buffer's pixels outside the view must be filled with the border color: copies write only the
    /// view, so a buffer keeps its borders until the placement changes.</param>
    public GpuBuffer Claim(long frame, in ViewPlacement placement, out bool fillBorders)
    {
        var index = (int)(frame % _buffers.Length);
        _frames[index] = frame;
        fillBorders = placement.HasBorders && _placements[index] != placement;
        _placements[index] = placement;
        return _buffers[index];
    }

    /// <summary>Gets the buffer holding <paramref name="frame"/>, or null if it was never rendered at this size or has been overwritten.</summary>
    public GpuBuffer? Find(long frame)
    {
        var index = (int)(frame % _buffers.Length);
        return frame >= 0 && _frames[index] == frame ? _buffers[index] : null;
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
            buffer.Dispose();
    }
}
