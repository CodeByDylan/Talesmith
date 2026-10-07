using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>A 2D color image in device memory with a view of it.</summary>
internal sealed unsafe class GpuImage : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly VkDeviceMemory _memory;

    public GpuImage(VulkanDevice device, int width, int height, VkFormat format, VkImageUsageFlags usage)
    {
        _device = device;
        Width = width;
        Height = height;
        var api = device.Api;
        var createInfo = new VkImageCreateInfo
        {
            imageType = VkImageType.Image2D,
            format = format,
            extent = new VkExtent3D((uint)width, (uint)height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = usage,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined
        };
        VkImage image;
        api.vkCreateImage(&createInfo, &image).CheckResult();
        Handle = image;

        VkMemoryRequirements requirements;
        api.vkGetImageMemoryRequirements(image, &requirements);
        var allocateInfo = new VkMemoryAllocateInfo
        {
            allocationSize = requirements.size,
            memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal, VkMemoryPropertyFlags.DeviceLocal, out _)
        };
        VkDeviceMemory memory;
        try
        {
            api.vkAllocateMemory(&allocateInfo, &memory).CheckResult();
        }
        catch
        {
            api.vkDestroyImage(image);
            throw;
        }

        _memory = memory;
        api.vkBindImageMemory(image, memory, 0).CheckResult();

        var viewInfo = new VkImageViewCreateInfo
        {
            image = image,
            viewType = VkImageViewType.Image2D,
            format = format,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1)
        };
        VkImageView view;
        api.vkCreateImageView(&viewInfo, &view).CheckResult();
        View = view;
    }

    public VkImage Handle { get; }

    public VkImageView View { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Records a layout change of the whole image, ordering <paramref name="sourceStage"/> work before <paramref name="destinationStage"/> work.</summary>
    public void Transition(VkCommandBuffer commands, VkImageLayout from, VkImageLayout to, VkPipelineStageFlags sourceStage, VkAccessFlags sourceAccess,
        VkPipelineStageFlags destinationStage, VkAccessFlags destinationAccess)
    {
        var barrier = new VkImageMemoryBarrier
        {
            srcAccessMask = sourceAccess,
            dstAccessMask = destinationAccess,
            oldLayout = from,
            newLayout = to,
            srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            image = Handle,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1)
        };
        _device.Api.vkCmdPipelineBarrier(commands, sourceStage, destinationStage, 0, 0, null, 0, null, 1, &barrier);
    }

    public void Dispose()
    {
        _device.Api.vkDestroyImageView(View);
        _device.Api.vkDestroyImage(Handle);
        _device.Api.vkFreeMemory(_memory);
    }
}
