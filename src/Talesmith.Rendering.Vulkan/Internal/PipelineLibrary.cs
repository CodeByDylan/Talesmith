using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Talesmith.Rendering.Lighting;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>The render passes, layouts and pipelines shared by every frame; pipelines are created on first use per blend mode and shader.</summary>
/// <remarks>
/// Descriptor set 0 holds the sampled image, set 1 the dynamic Effect uniform block, set 2 the shadow map and set 3 the occluder mask;
/// only light pipelines read the last two. Push constants carry the view transform, the target size and the inverse texture size to
/// the sprite vertex shader.
/// </remarks>
internal sealed unsafe partial class PipelineLibrary : IDisposable
{
    public const VkFormat TargetFormat = VkFormat.B8G8R8A8Unorm;
    public const uint PushConstantsSize = 40;

    /// <summary>Light maps hold light above 1 at full precision; they store half the light.</summary>
    public const VkFormat LightMapFormat = VkFormat.R16G16B16A16Sfloat;

    private readonly VulkanDevice _device;
    private readonly ShaderLibrary _shaders;
    private readonly ILogger _logger;
    private readonly Dictionary<PipelineKey, PipelineEntry> _pipelines = [];
    private readonly VkPipeline[] _lightPipelines = new VkPipeline[3];
    private VkPipeline _emissivePipeline;
    private VkPipeline _compositePipeline;

    public PipelineLibrary(VulkanDevice device, ShaderLibrary shaders, ILogger logger)
    {
        _device = device;
        _shaders = shaders;
        _logger = logger;
        ScenePass = CreateRenderPass(VkAttachmentLoadOp.Clear, TargetFormat, VkImageLayout.ColorAttachmentOptimal);
        EffectPass = CreateRenderPass(VkAttachmentLoadOp.DontCare, TargetFormat, VkImageLayout.ColorAttachmentOptimal);
        LightPass = CreateRenderPass(VkAttachmentLoadOp.Clear, LightMapFormat, VkImageLayout.ShaderReadOnlyOptimal);
        TextureLayout = CreateSetLayout(VkDescriptorType.CombinedImageSampler);
        EffectLayout = CreateSetLayout(VkDescriptorType.UniformBufferDynamic);

        var setLayouts = stackalloc VkDescriptorSetLayout[] { TextureLayout, EffectLayout, TextureLayout, TextureLayout };
        var pushConstants = new VkPushConstantRange { stageFlags = VkShaderStageFlags.Vertex, offset = 0, size = PushConstantsSize };
        var layoutInfo = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = 4,
            pSetLayouts = setLayouts,
            pushConstantRangeCount = 1,
            pPushConstantRanges = &pushConstants
        };
        VkPipelineLayout layout;
        device.Api.vkCreatePipelineLayout(&layoutInfo, &layout).CheckResult();
        Layout = layout;
    }

    /// <summary>Clears the target, then draws.</summary>
    public VkRenderPass ScenePass { get; }

    /// <summary>Overwrites the whole target without reading it first.</summary>
    public VkRenderPass EffectPass { get; }

    /// <summary>Clears a light map to the ambient light, draws lights into it and leaves it ready for sampling.</summary>
    public VkRenderPass LightPass { get; }

    public VkDescriptorSetLayout TextureLayout { get; }

    public VkDescriptorSetLayout EffectLayout { get; }

    public VkPipelineLayout Layout { get; }

    /// <summary>Gets the sprite pipeline for a blend mode and optional material shader, falling back to the default shader when it is unusable.</summary>
    public PipelineEntry GetSprite(BlendMode blend, ShaderSource? shader)
    {
        var key = new PipelineKey(shader, blend, PostEffect: false);
        if (_pipelines.TryGetValue(key, out var entry))
            return entry;

        if (shader is null)
            entry = new PipelineEntry(CreateDefaultSprite(blend), UsesEffectData: false);
        else if (_shaders.TryGetFragment(shader, out var fragment)
                 && TryCreatePipeline(shader.Name, _shaders.SpriteVertex, fragment, blend, postEffect: false, out var pipeline))
            entry = new PipelineEntry(pipeline, UsesEffectData: true);
        else
            entry = GetSprite(blend, null) with { IsShared = true };
        _pipelines[key] = entry;
        return entry;
    }

    /// <summary>Gets the pipeline for a post effect, or a null pipeline when its shader is unusable and the effect is skipped.</summary>
    public VkPipeline GetPostEffect(ShaderSource shader)
    {
        var key = new PipelineKey(shader, BlendMode.Opaque, PostEffect: true);
        if (_pipelines.TryGetValue(key, out var entry))
            return entry.Pipeline;

        var pipeline = _shaders.TryGetFragment(shader, out var fragment)
            && TryCreatePipeline(shader.Name, _shaders.FullScreenVertex, fragment, BlendMode.Opaque, postEffect: true, out var created)
                ? created
                : VkPipeline.Null;
        _pipelines[key] = new PipelineEntry(pipeline, UsesEffectData: true);
        return pipeline;
    }

    /// <summary>Gets the light map pipeline for lights of one blend mode.</summary>
    public VkPipeline GetLight(LightBlend blend)
    {
        ref var pipeline = ref _lightPipelines[(int)blend];
        if (pipeline == VkPipeline.Null)
            pipeline = CreateBuiltIn("light", _shaders.SpriteVertex, _shaders.LightFragment, LightBlendState(blend), postEffect: false, LightPass);
        return pipeline;
    }

    /// <summary>Adds glowing sprite masks to the light map.</summary>
    public VkPipeline Emissive
    {
        get
        {
            if (_emissivePipeline == VkPipeline.Null)
                _emissivePipeline = CreateBuiltIn("emissive", _shaders.SpriteVertex, _shaders.EmissiveFragment, LightBlendState(LightBlend.Additive),
                    postEffect: false, LightPass);
            return _emissivePipeline;
        }
    }

    /// <summary>Multiplies the scene target by twice the light map, keeping its alpha.</summary>
    public VkPipeline Composite
    {
        get
        {
            if (_compositePipeline == VkPipeline.Null)
            {
                var blend = new VkPipelineColorBlendAttachmentState
                {
                    colorWriteMask = VkColorComponentFlags.All,
                    blendEnable = true,
                    colorBlendOp = VkBlendOp.Add,
                    alphaBlendOp = VkBlendOp.Add,
                    srcColorBlendFactor = VkBlendFactor.DstColor,
                    dstColorBlendFactor = VkBlendFactor.SrcColor,
                    srcAlphaBlendFactor = VkBlendFactor.Zero,
                    dstAlphaBlendFactor = VkBlendFactor.One
                };
                _compositePipeline = CreateBuiltIn("light composite", _shaders.FullScreenVertex, _shaders.CompositeFragment, blend, postEffect: true, ScenePass);
            }

            return _compositePipeline;
        }
    }

    public void Dispose()
    {
        var api = _device.Api;
        foreach (var entry in _pipelines.Values)
        {
            if (entry.Pipeline != VkPipeline.Null && !entry.IsShared)
                api.vkDestroyPipeline(entry.Pipeline);
        }

        _pipelines.Clear();
        foreach (var pipeline in _lightPipelines)
        {
            if (pipeline != VkPipeline.Null)
                api.vkDestroyPipeline(pipeline);
        }

        if (_emissivePipeline != VkPipeline.Null)
            api.vkDestroyPipeline(_emissivePipeline);
        if (_compositePipeline != VkPipeline.Null)
            api.vkDestroyPipeline(_compositePipeline);
        api.vkDestroyPipelineLayout(Layout);
        api.vkDestroyDescriptorSetLayout(TextureLayout);
        api.vkDestroyDescriptorSetLayout(EffectLayout);
        api.vkDestroyRenderPass(ScenePass);
        api.vkDestroyRenderPass(EffectPass);
        api.vkDestroyRenderPass(LightPass);
    }

    private VkPipeline CreateDefaultSprite(BlendMode blend)
    {
        if (!TryCreatePipeline("default sprite", _shaders.SpriteVertex, _shaders.SpriteFragment, blend, postEffect: false, out var pipeline))
            throw new InvalidOperationException("The built-in sprite pipeline could not be created.");
        return pipeline;
    }

    private VkPipeline CreateBuiltIn(string name, VkShaderModule vertex, VkShaderModule fragment, in VkPipelineColorBlendAttachmentState blend,
        bool postEffect, VkRenderPass renderPass)
    {
        if (!TryCreatePipeline(name, vertex, fragment, blend, postEffect, renderPass, out var pipeline))
            throw new InvalidOperationException($"The built-in {name} pipeline could not be created.");
        return pipeline;
    }

    /// <summary>Additive lights add, mix lights blend over with their alpha, multiply lights multiply the light map.</summary>
    private static VkPipelineColorBlendAttachmentState LightBlendState(LightBlend blend)
    {
        var state = new VkPipelineColorBlendAttachmentState
        {
            colorWriteMask = VkColorComponentFlags.All,
            blendEnable = true,
            colorBlendOp = VkBlendOp.Add,
            alphaBlendOp = VkBlendOp.Add
        };
        (state.srcColorBlendFactor, state.dstColorBlendFactor, state.srcAlphaBlendFactor, state.dstAlphaBlendFactor) = blend switch
        {
            LightBlend.Mix => (VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha, VkBlendFactor.Zero, VkBlendFactor.One),
            LightBlend.Multiply => (VkBlendFactor.DstColor, VkBlendFactor.Zero, VkBlendFactor.Zero, VkBlendFactor.One),
            _ => (VkBlendFactor.One, VkBlendFactor.One, VkBlendFactor.Zero, VkBlendFactor.One)
        };
        return state;
    }

    private bool TryCreatePipeline(string name, VkShaderModule vertex, VkShaderModule fragment, BlendMode blend, bool postEffect, out VkPipeline pipeline) =>
        TryCreatePipeline(name, vertex, fragment, CreateBlendState(blend), postEffect, ScenePass, out pipeline);

    private bool TryCreatePipeline(string name, VkShaderModule vertex, VkShaderModule fragment, VkPipelineColorBlendAttachmentState blendAttachment,
        bool postEffect, VkRenderPass renderPass, out VkPipeline pipeline)
    {
        var entryPoint = (byte*)Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetReference("main"u8));
        var stages = stackalloc VkPipelineShaderStageCreateInfo[]
        {
            new() { stage = VkShaderStageFlags.Vertex, module = vertex, pName = entryPoint },
            new() { stage = VkShaderStageFlags.Fragment, module = fragment, pName = entryPoint }
        };

        var binding = new VkVertexInputBindingDescription
        {
            binding = 0,
            stride = (uint)Unsafe.SizeOf<SpriteInstance>(),
            inputRate = VkVertexInputRate.Instance
        };
        var attributes = stackalloc VkVertexInputAttributeDescription[]
        {
            new() { location = 0, format = VkFormat.R32G32Sfloat, offset = 0 },
            new() { location = 1, format = VkFormat.R32G32Sfloat, offset = 8 },
            new() { location = 2, format = VkFormat.R32G32Sfloat, offset = 16 },
            new() { location = 3, format = VkFormat.R32G32B32A32Sfloat, offset = 24 },
            new() { location = 4, format = VkFormat.R8G8B8A8Unorm, offset = 40 }
        };
        var vertexInput = postEffect
            ? new VkPipelineVertexInputStateCreateInfo()
            : new VkPipelineVertexInputStateCreateInfo
            {
                vertexBindingDescriptionCount = 1,
                pVertexBindingDescriptions = &binding,
                vertexAttributeDescriptionCount = 5,
                pVertexAttributeDescriptions = attributes
            };
        var inputAssembly = new VkPipelineInputAssemblyStateCreateInfo
        {
            topology = postEffect ? VkPrimitiveTopology.TriangleList : VkPrimitiveTopology.TriangleStrip
        };
        var viewportState = new VkPipelineViewportStateCreateInfo { viewportCount = 1, scissorCount = 1 };
        var rasterization = new VkPipelineRasterizationStateCreateInfo
        {
            polygonMode = VkPolygonMode.Fill,
            cullMode = VkCullModeFlags.None,
            frontFace = VkFrontFace.CounterClockwise,
            lineWidth = 1
        };
        var multisample = new VkPipelineMultisampleStateCreateInfo { rasterizationSamples = VkSampleCountFlags.Count1 };
        var colorBlend = new VkPipelineColorBlendStateCreateInfo { attachmentCount = 1, pAttachments = &blendAttachment };
        var dynamicStates = stackalloc VkDynamicState[] { VkDynamicState.Viewport, VkDynamicState.Scissor };
        var dynamicState = new VkPipelineDynamicStateCreateInfo { dynamicStateCount = 2, pDynamicStates = dynamicStates };

        var createInfo = new VkGraphicsPipelineCreateInfo
        {
            stageCount = 2,
            pStages = stages,
            pVertexInputState = &vertexInput,
            pInputAssemblyState = &inputAssembly,
            pViewportState = &viewportState,
            pRasterizationState = &rasterization,
            pMultisampleState = &multisample,
            pColorBlendState = &colorBlend,
            pDynamicState = &dynamicState,
            layout = Layout,
            renderPass = renderPass,
            subpass = 0
        };
        VkPipeline created;
        var result = _device.Api.vkCreateGraphicsPipelines(VkPipelineCache.Null, 1, &createInfo, &created);
        if (result != VkResult.Success)
        {
            LogPipelineFailed(_logger, name, result);
            pipeline = VkPipeline.Null;
            return false;
        }

        pipeline = created;
        return true;
    }

    private static VkPipelineColorBlendAttachmentState CreateBlendState(BlendMode blend)
    {
        var state = new VkPipelineColorBlendAttachmentState
        {
            colorWriteMask = VkColorComponentFlags.All,
            blendEnable = blend != BlendMode.Opaque,
            colorBlendOp = VkBlendOp.Add,
            alphaBlendOp = VkBlendOp.Add
        };
        (state.srcColorBlendFactor, state.dstColorBlendFactor, state.srcAlphaBlendFactor, state.dstAlphaBlendFactor) = blend switch
        {
            BlendMode.Additive => (VkBlendFactor.One, VkBlendFactor.One, VkBlendFactor.One, VkBlendFactor.One),
            BlendMode.Multiply => (VkBlendFactor.DstColor, VkBlendFactor.OneMinusSrcAlpha, VkBlendFactor.DstAlpha, VkBlendFactor.OneMinusSrcAlpha),
            BlendMode.Opaque => (VkBlendFactor.One, VkBlendFactor.Zero, VkBlendFactor.One, VkBlendFactor.Zero),
            _ => (VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha, VkBlendFactor.One, VkBlendFactor.OneMinusSrcAlpha)
        };
        return state;
    }

    private VkRenderPass CreateRenderPass(VkAttachmentLoadOp loadOp, VkFormat format, VkImageLayout finalLayout)
    {
        var attachment = new VkAttachmentDescription
        {
            format = format,
            samples = VkSampleCountFlags.Count1,
            loadOp = loadOp,
            storeOp = VkAttachmentStoreOp.Store,
            stencilLoadOp = VkAttachmentLoadOp.DontCare,
            stencilStoreOp = VkAttachmentStoreOp.DontCare,
            initialLayout = VkImageLayout.Undefined,
            finalLayout = finalLayout
        };
        var colorReference = new VkAttachmentReference { attachment = 0, layout = VkImageLayout.ColorAttachmentOptimal };
        var subpass = new VkSubpassDescription
        {
            pipelineBindPoint = VkPipelineBindPoint.Graphics,
            colorAttachmentCount = 1,
            pColorAttachments = &colorReference
        };
        // Targets are reused by consecutive frames and ping-pong passes, so earlier reads and writes of them must finish first.
        var dependencies = stackalloc VkSubpassDependency[2];
        dependencies[0] = new VkSubpassDependency
        {
            srcSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL,
            dstSubpass = 0,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput | VkPipelineStageFlags.FragmentShader | VkPipelineStageFlags.Transfer,
            dstStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
            dstAccessMask = VkAccessFlags.ColorAttachmentRead | VkAccessFlags.ColorAttachmentWrite
        };
        // Passes that end ready for sampling are read by the fragment shaders of later passes.
        dependencies[1] = new VkSubpassDependency
        {
            srcSubpass = 0,
            dstSubpass = Vortice.Vulkan.Vulkan.VK_SUBPASS_EXTERNAL,
            srcStageMask = VkPipelineStageFlags.ColorAttachmentOutput,
            dstStageMask = VkPipelineStageFlags.FragmentShader,
            srcAccessMask = VkAccessFlags.ColorAttachmentWrite,
            dstAccessMask = VkAccessFlags.ShaderRead
        };
        var createInfo = new VkRenderPassCreateInfo
        {
            attachmentCount = 1,
            pAttachments = &attachment,
            subpassCount = 1,
            pSubpasses = &subpass,
            dependencyCount = finalLayout == VkImageLayout.ShaderReadOnlyOptimal ? 2u : 1u,
            pDependencies = dependencies
        };
        VkRenderPass renderPass;
        _device.Api.vkCreateRenderPass(&createInfo, &renderPass).CheckResult();
        return renderPass;
    }

    private VkDescriptorSetLayout CreateSetLayout(VkDescriptorType type)
    {
        var binding = new VkDescriptorSetLayoutBinding
        {
            binding = 0,
            descriptorType = type,
            descriptorCount = 1,
            stageFlags = VkShaderStageFlags.Fragment
        };
        var createInfo = new VkDescriptorSetLayoutCreateInfo { bindingCount = 1, pBindings = &binding };
        VkDescriptorSetLayout layout;
        _device.Api.vkCreateDescriptorSetLayout(&createInfo, &layout).CheckResult();
        return layout;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Vulkan pipeline for shader '{Name}' could not be created ({Result}); the default shader is used instead.")]
    private static partial void LogPipelineFailed(ILogger logger, string name, VkResult result);

    private readonly record struct PipelineKey(ShaderSource? Shader, BlendMode Blend, bool PostEffect);
}

/// <summary>A pipeline and whether its fragment shader reads the Effect uniform block.</summary>
internal readonly record struct PipelineEntry(VkPipeline Pipeline, bool UsesEffectData)
{
    /// <summary>Whether this entry reuses another key's default pipeline, so it must not be destroyed twice.</summary>
    public bool IsShared { get; init; }
}
