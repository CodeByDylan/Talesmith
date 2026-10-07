using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Where a buffer's memory lives and how the CPU reaches it.</summary>
internal enum BufferMemory
{
    /// <summary>GPU memory the CPU never touches, filled by copies.</summary>
    Device,

    /// <summary>Mapped memory the CPU writes and the GPU reads, in GPU memory when the device allows it.</summary>
    Upload,

    /// <summary>Mapped memory the GPU writes and the CPU reads, cached for fast reads when possible.</summary>
    Readback
}

/// <summary>A buffer with its own memory allocation, persistently mapped unless it is device memory.</summary>
internal sealed unsafe class GpuBuffer : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly VkDeviceMemory _memory;
    private readonly bool _coherent;

    public GpuBuffer(VulkanDevice device, ulong size, VkBufferUsageFlags usage, BufferMemory memory)
    {
        _device = device;
        Size = size;
        var api = device.Api;
        var createInfo = new VkBufferCreateInfo { size = size, usage = usage, sharingMode = VkSharingMode.Exclusive };
        VkBuffer buffer;
        api.vkCreateBuffer(&createInfo, &buffer).CheckResult();
        Handle = buffer;

        VkMemoryRequirements requirements;
        api.vkGetBufferMemoryRequirements(buffer, &requirements);
        var (required, preferred) = memory switch
        {
            BufferMemory.Upload => (VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent,
                VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent | VkMemoryPropertyFlags.DeviceLocal),
            BufferMemory.Readback => (VkMemoryPropertyFlags.HostVisible, VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCached),
            _ => (VkMemoryPropertyFlags.DeviceLocal, VkMemoryPropertyFlags.DeviceLocal)
        };
        var allocateInfo = new VkMemoryAllocateInfo
        {
            allocationSize = requirements.size,
            memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits, required, preferred, out var flags)
        };
        _coherent = (flags & VkMemoryPropertyFlags.HostCoherent) != 0;
        VkDeviceMemory deviceMemory;
        try
        {
            api.vkAllocateMemory(&allocateInfo, &deviceMemory).CheckResult();
        }
        catch
        {
            api.vkDestroyBuffer(buffer);
            throw;
        }

        _memory = deviceMemory;
        api.vkBindBufferMemory(buffer, deviceMemory, 0).CheckResult();
        if (memory != BufferMemory.Device)
        {
            void* mapped;
            api.vkMapMemory(deviceMemory, 0, Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE, 0, &mapped).CheckResult();
            Mapped = (byte*)mapped;
        }
    }

    public VkBuffer Handle { get; }

    public ulong Size { get; }

    /// <summary>The CPU address of the buffer's memory, or null for device memory.</summary>
    public byte* Mapped { get; }

    /// <summary>Makes GPU writes visible to the CPU; needed only for non-coherent memory.</summary>
    public void InvalidateForRead()
    {
        if (_coherent)
            return;
        var range = new VkMappedMemoryRange { memory = _memory, offset = 0, size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE };
        _device.Api.vkInvalidateMappedMemoryRanges(1, &range).CheckResult();
    }

    public void Dispose()
    {
        _device.Api.vkDestroyBuffer(Handle);
        _device.Api.vkFreeMemory(_memory);
    }
}
