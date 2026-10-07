using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Allocates individually freeable descriptor sets, adding pools as they fill up.</summary>
internal sealed unsafe class DescriptorAllocator(VulkanDevice device) : IDisposable
{
    private const uint SetsPerPool = 256;

    private readonly List<VkDescriptorPool> _pools = [];

    public DescriptorSetAllocation Allocate(VkDescriptorSetLayout layout)
    {
        for (var i = _pools.Count - 1; i >= 0; i--)
        {
            if (TryAllocate(_pools[i], layout, out var set))
                return new DescriptorSetAllocation(_pools[i], set);
        }

        var pool = CreatePool();
        _pools.Add(pool);
        if (!TryAllocate(pool, layout, out var created))
            throw new VkException(VkResult.ErrorOutOfPoolMemory, "A new descriptor pool could not allocate a set.");
        return new DescriptorSetAllocation(pool, created);
    }

    public void Free(DescriptorSetAllocation allocation)
    {
        var set = allocation.Set;
        device.Api.vkFreeDescriptorSets(allocation.Pool, 1, &set).CheckResult();
    }

    public void Dispose()
    {
        foreach (var pool in _pools)
            device.Api.vkDestroyDescriptorPool(pool);
        _pools.Clear();
    }

    private bool TryAllocate(VkDescriptorPool pool, VkDescriptorSetLayout layout, out VkDescriptorSet set)
    {
        var allocateInfo = new VkDescriptorSetAllocateInfo { descriptorPool = pool, descriptorSetCount = 1, pSetLayouts = &layout };
        VkDescriptorSet allocated;
        var result = device.Api.vkAllocateDescriptorSets(&allocateInfo, &allocated);
        set = allocated;
        if (result is VkResult.ErrorOutOfPoolMemory or VkResult.ErrorFragmentedPool)
            return false;
        result.CheckResult();
        return true;
    }

    private VkDescriptorPool CreatePool()
    {
        var sizes = stackalloc VkDescriptorPoolSize[]
        {
            new() { type = VkDescriptorType.CombinedImageSampler, descriptorCount = SetsPerPool },
            new() { type = VkDescriptorType.UniformBufferDynamic, descriptorCount = 16 }
        };
        var createInfo = new VkDescriptorPoolCreateInfo
        {
            flags = VkDescriptorPoolCreateFlags.FreeDescriptorSet,
            maxSets = SetsPerPool,
            poolSizeCount = 2,
            pPoolSizes = sizes
        };
        VkDescriptorPool pool;
        device.Api.vkCreateDescriptorPool(&createInfo, &pool).CheckResult();
        return pool;
    }
}

internal readonly record struct DescriptorSetAllocation(VkDescriptorPool Pool, VkDescriptorSet Set);
