using System.Numerics;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>A sampled image with its own descriptor set, ready to bind as set 0.</summary>
internal sealed unsafe class GpuTexture : IDisposable
{
    private readonly DescriptorAllocator _descriptors;
    private readonly DescriptorSetAllocation _allocation;

    public GpuTexture(VulkanDevice device, DescriptorAllocator descriptors, VkDescriptorSetLayout layout, GpuImage image, VkSampler sampler)
    {
        _descriptors = descriptors;
        Image = image;
        InverseSize = new Vector2(1f / image.Width, 1f / image.Height);
        _allocation = descriptors.Allocate(layout);
        WriteImageDescriptor(device, _allocation.Set, image.View, sampler);
    }

    public GpuImage Image { get; }

    public VkDescriptorSet DescriptorSet => _allocation.Set;

    public Vector2 InverseSize { get; }

    public static void WriteImageDescriptor(VulkanDevice device, VkDescriptorSet set, VkImageView view, VkSampler sampler)
    {
        var imageInfo = new VkDescriptorImageInfo { sampler = sampler, imageView = view, imageLayout = VkImageLayout.ShaderReadOnlyOptimal };
        var write = new VkWriteDescriptorSet
        {
            dstSet = set,
            dstBinding = 0,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.CombinedImageSampler,
            pImageInfo = &imageInfo
        };
        device.Api.vkUpdateDescriptorSets(1, &write, 0, null);
    }

    public void Dispose()
    {
        _descriptors.Free(_allocation);
        Image.Dispose();
    }
}
