using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>The two edge-clamped samplers every texture uses: smooth and nearest-texel.</summary>
internal sealed unsafe class Samplers : IDisposable
{
    private readonly VulkanDevice _device;

    public Samplers(VulkanDevice device)
    {
        _device = device;
        Linear = Create(VkFilter.Linear);
        Nearest = Create(VkFilter.Nearest);
    }

    public VkSampler Linear { get; }

    public VkSampler Nearest { get; }

    public VkSampler For(TextureFilter filter) => filter == TextureFilter.Nearest ? Nearest : Linear;

    public void Dispose()
    {
        _device.Api.vkDestroySampler(Linear);
        _device.Api.vkDestroySampler(Nearest);
    }

    private VkSampler Create(VkFilter filter)
    {
        var createInfo = new VkSamplerCreateInfo
        {
            magFilter = filter,
            minFilter = filter,
            mipmapMode = VkSamplerMipmapMode.Nearest,
            addressModeU = VkSamplerAddressMode.ClampToEdge,
            addressModeV = VkSamplerAddressMode.ClampToEdge,
            addressModeW = VkSamplerAddressMode.ClampToEdge,
            maxLod = 0
        };
        VkSampler sampler;
        _device.Api.vkCreateSampler(&createInfo, &sampler).CheckResult();
        return sampler;
    }
}
