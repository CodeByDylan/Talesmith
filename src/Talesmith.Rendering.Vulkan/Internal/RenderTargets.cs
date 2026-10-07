using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Two same-sized offscreen color targets: the scene is drawn into one and post effects ping-pong between them.</summary>
internal sealed unsafe class RenderTargets : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly DescriptorAllocator _descriptors;
    private readonly GpuImage[] _images = new GpuImage[2];
    private readonly VkFramebuffer[] _framebuffers = new VkFramebuffer[2];
    private readonly DescriptorSetAllocation[] _sampleSets = new DescriptorSetAllocation[2];

    public RenderTargets(VulkanDevice device, DescriptorAllocator descriptors, PipelineLibrary pipelines, VkSampler sampler, int width, int height)
    {
        _device = device;
        _descriptors = descriptors;
        Width = width;
        Height = height;
        for (var i = 0; i < 2; i++)
        {
            var image = new GpuImage(device, width, height, PipelineLibrary.TargetFormat,
                VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferSrc);
            _images[i] = image;
            var view = image.View;
            var framebufferInfo = new VkFramebufferCreateInfo
            {
                renderPass = pipelines.ScenePass,
                attachmentCount = 1,
                pAttachments = &view,
                width = (uint)width,
                height = (uint)height,
                layers = 1
            };
            VkFramebuffer framebuffer;
            device.Api.vkCreateFramebuffer(&framebufferInfo, &framebuffer).CheckResult();
            _framebuffers[i] = framebuffer;
            _sampleSets[i] = descriptors.Allocate(pipelines.TextureLayout);
            GpuTexture.WriteImageDescriptor(device, _sampleSets[i].Set, view, sampler);
        }
    }

    public int Width { get; }

    public int Height { get; }

    public GpuImage Image(int index) => _images[index];

    public VkFramebuffer Framebuffer(int index) => _framebuffers[index];

    /// <summary>Set 0 for sampling target <paramref name="index"/> in a post effect.</summary>
    public VkDescriptorSet SampleSet(int index) => _sampleSets[index].Set;

    public void Dispose()
    {
        for (var i = 0; i < 2; i++)
        {
            _descriptors.Free(_sampleSets[i]);
            _device.Api.vkDestroyFramebuffer(_framebuffers[i]);
            _images[i].Dispose();
        }
    }
}
