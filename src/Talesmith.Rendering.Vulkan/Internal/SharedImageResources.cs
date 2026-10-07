using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>An exportable image with its memory and the two exportable semaphores that hand it to another graphics API and back.</summary>
internal sealed unsafe class SharedImageResources : IDisposable
{
    private const VkExternalMemoryHandleTypeFlags MemoryHandleType = VkExternalMemoryHandleTypeFlags.OpaqueFD;
    private const VkExternalSemaphoreHandleTypeFlags SemaphoreHandleType = VkExternalSemaphoreHandleTypeFlags.OpaqueFD;

    private readonly VulkanDevice _device;
    private readonly VkDeviceMemory _memory;

    /// <param name="dedicatedAllocation">Gives the image its own allocation; importers must then import it as dedicated too.</param>
    public SharedImageResources(VulkanDevice device, int width, int height, bool dedicatedAllocation)
    {
        _device = device;
        Width = width;
        Height = height;
        var api = device.Api;

        var external = new VkExternalMemoryImageCreateInfo { handleTypes = MemoryHandleType };
        var createInfo = new VkImageCreateInfo
        {
            pNext = &external,
            imageType = VkImageType.Image2D,
            format = VulkanDevice.SharedImageFormat,
            extent = new VkExtent3D((uint)width, (uint)height, 1),
            mipLevels = 1,
            arrayLayers = 1,
            samples = VkSampleCountFlags.Count1,
            tiling = VkImageTiling.Optimal,
            usage = VulkanDevice.SharedImageUsage,
            flags = VulkanDevice.SharedImageFlags,
            sharingMode = VkSharingMode.Exclusive,
            initialLayout = VkImageLayout.Undefined
        };
        VkImage image;
        api.vkCreateImage(&createInfo, &image).CheckResult();
        Image = image;

        try
        {
            VkMemoryRequirements requirements;
            api.vkGetImageMemoryRequirements(image, &requirements);
            var dedicated = new VkMemoryDedicatedAllocateInfo { image = image };
            var export = new VkExportMemoryAllocateInfo { pNext = dedicatedAllocation ? &dedicated : null, handleTypes = MemoryHandleType };
            var allocateInfo = new VkMemoryAllocateInfo
            {
                pNext = &export,
                allocationSize = requirements.size,
                memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits, VkMemoryPropertyFlags.DeviceLocal, VkMemoryPropertyFlags.DeviceLocal, out _)
            };
            VkDeviceMemory memory;
            api.vkAllocateMemory(&allocateInfo, &memory).CheckResult();
            _memory = memory;
            MemorySize = requirements.size;
            api.vkBindImageMemory(image, memory, 0).CheckResult();
            Ready = CreateSemaphore();
            Released = CreateSemaphore();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public VkImage Image { get; }

    public int Width { get; }

    public int Height { get; }

    public ulong MemorySize { get; }

    /// <summary>Signaled by the renderer once a frame is in the image.</summary>
    public VkSemaphore Ready { get; }

    /// <summary>Signaled by the other API once it no longer reads the image.</summary>
    public VkSemaphore Released { get; }

    public int ExportMemory()
    {
        var info = new VkMemoryGetFdInfoKHR { memory = _memory, handleType = MemoryHandleType };
        int fd;
        _device.Api.vkGetMemoryFdKHR(&info, &fd).CheckResult();
        return fd;
    }

    public int ExportSemaphore(VkSemaphore semaphore)
    {
        var info = new VkSemaphoreGetFdInfoKHR { semaphore = semaphore, handleType = SemaphoreHandleType };
        int fd;
        _device.Api.vkGetSemaphoreFdKHR(&info, &fd).CheckResult();
        return fd;
    }

    public void Dispose()
    {
        var api = _device.Api;
        if (Released != VkSemaphore.Null)
            api.vkDestroySemaphore(Released);
        if (Ready != VkSemaphore.Null)
            api.vkDestroySemaphore(Ready);
        if (Image != VkImage.Null)
            api.vkDestroyImage(Image);
        if (_memory != VkDeviceMemory.Null)
            api.vkFreeMemory(_memory);
    }

    private VkSemaphore CreateSemaphore()
    {
        var export = new VkExportSemaphoreCreateInfo { handleTypes = SemaphoreHandleType };
        var createInfo = new VkSemaphoreCreateInfo { pNext = &export };
        VkSemaphore semaphore;
        _device.Api.vkCreateSemaphore(&createInfo, &semaphore).CheckResult();
        return semaphore;
    }
}
