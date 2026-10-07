using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.Rendering.Lighting;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Records a frame's light map: the shadow map and occluder mask uploads, then every light and glowing sprite into a half-float
/// target.</summary>
/// <remarks>
/// Lights are drawn as instanced quads with the sprite vertex shader and <c>light.frag</c>, reading their parameters from the Effect
/// block, the shadow map at set 2 and the occluder mask at set 3. The scene recorder then multiplies the lit batches by the light map
/// with <see cref="PipelineLibrary.Composite"/>.
/// </remarks>
internal sealed unsafe class LightMapRecorder(VulkanDevice device, DescriptorAllocator descriptors, PipelineLibrary pipelines, TextureStore textures,
    Samplers samplers, DeferredReleaser releaser, Texture whiteTexture) : IDisposable
{
    private readonly ShadowMapBuilder _shadows = new();
    private readonly OccluderMaskBuilder _occluders = new();
    private SpriteInstance[] _instances = new SpriteInstance[64];
    private int _instanceCount;
    private int _lightCount;
    private LightMapTarget? _target;
    private UploadImage? _shadowImage;
    private UploadImage? _occluderImage;
    private bool _hasShadows;

    /// <summary>Whether the frame given to <see cref="Prepare"/> is lit.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The instances the light pass draws, uploaded after the frame's own instances.</summary>
    public ReadOnlySpan<SpriteInstance> Instances => _instances.AsSpan(0, _instanceCount);

    /// <summary>The Effect blocks the light pass writes.</summary>
    public int EffectBlocks => IsActive ? _lightCount : 0;

    /// <summary>The set that samples the light map in the composite pass.</summary>
    public VkDescriptorSet LightMapSet => _target!.SampleSet.Set;

    /// <summary>Builds the frame's light quads and shadow map on the CPU; call before uploading instances.</summary>
    public void Prepare(RenderFrame frame)
    {
        var lighting = frame.Lighting;
        IsActive = lighting.IsActive;
        _instanceCount = 0;
        _lightCount = 0;
        _hasShadows = false;
        if (!IsActive)
            return;

        EnsureCapacity(lighting.EmissiveInstances.Length + lighting.Lights.Length);
        lighting.EmissiveInstances.CopyTo(_instances);
        _instanceCount = lighting.EmissiveInstances.Length;
        foreach (ref readonly var light in lighting.Lights)
        {
            _instances[_instanceCount++] = LightShaderData.Quad(light, frame.VisibleBounds);
            _lightCount++;
            _hasShadows |= light.CastsShadows;
        }

        _hasShadows &= !lighting.Occluders.IsEmpty;
        if (_hasShadows)
            _shadows.Build(lighting);
    }

    /// <summary>Records the shadow map and occluder mask uploads and the light pass; the frame's instances must already be in the instance
    /// buffer.</summary>
    /// <param name="firstInstance">Where <see cref="Instances"/> start in the instance buffer.</param>
    public void Record(FrameResources resources, RenderFrame frame, int width, int height, int firstInstance, long frameNumber)
    {
        var lighting = frame.Lighting;
        var settings = lighting.Settings;
        var mapWidth = Math.Max(1, (int)MathF.Ceiling(width * settings.ResolutionScale));
        var mapHeight = Math.Max(1, (int)MathF.Ceiling(height * settings.ResolutionScale));
        var view = frame.ViewMatrix * Matrix3x2.CreateScale((float)mapWidth / width, (float)mapHeight / height);
        EnsureTarget(mapWidth, mapHeight, frameNumber);
        if (_hasShadows)
        {
            UploadShadowMap(resources, frameNumber);
            UploadOccluderMask(resources, lighting, view, mapWidth, mapHeight, frameNumber);
        }

        var api = device.Api;
        var commands = resources.Commands;
        var ambient = settings.Ambient / LightMapSettings.MaxBrightness;
        var clear = new VkClearValue(ambient.X, ambient.Y, ambient.Z, 1);
        var beginInfo = new VkRenderPassBeginInfo
        {
            renderPass = pipelines.LightPass,
            framebuffer = _target!.Framebuffer,
            renderArea = new VkRect2D(0, 0, (uint)mapWidth, (uint)mapHeight),
            clearValueCount = 1,
            pClearValues = &clear
        };
        api.vkCmdBeginRenderPass(commands, &beginInfo, VkSubpassContents.Inline);
        var viewport = new VkViewport { x = 0, y = 0, width = mapWidth, height = mapHeight, minDepth = 0, maxDepth = 1 };
        var scissor = new VkRect2D(0, 0, (uint)mapWidth, (uint)mapHeight);
        api.vkCmdSetViewport(commands, 0, 1, &viewport);
        api.vkCmdSetScissor(commands, 0, 1, &scissor);

        var buffer = resources.InstanceBuffer;
        ulong bufferOffset = 0;
        api.vkCmdBindVertexBuffers(commands, 0, 1, &buffer, &bufferOffset);
        var mapSize = new Vector2(mapWidth, mapHeight);

        var lightsFirst = firstInstance + lighting.EmissiveInstances.Length;
        DrawLights(resources, lighting, view, mapSize, frame.Camera.Zoom * settings.ResolutionScale, lightsFirst);
        DrawEmissive(resources, lighting, view, mapSize, firstInstance);
        api.vkCmdEndRenderPass(commands);
    }

    public void Dispose()
    {
        _target?.Dispose();
        _shadowImage?.Dispose();
        _occluderImage?.Dispose();
    }

    private void DrawLights(FrameResources resources, LightingFrame lighting, in Matrix3x2 view, Vector2 mapSize, float pixelsPerUnit, int first)
    {
        var api = device.Api;
        var commands = resources.Commands;
        var layout = pipelines.Layout;
        var settings = lighting.Settings;
        var white = textures.Get(whiteTexture.Id)!;
        var constants = SpriteConstants.For(view, mapSize, Vector2.One);
        api.vkCmdPushConstants(commands, layout, VkShaderStageFlags.Vertex, 0, PipelineLibrary.PushConstantsSize, &constants);
        var shadowSet = _hasShadows ? _shadowImage!.SampleSet.Set : white.DescriptorSet;
        api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 2, 1, &shadowSet, 0, null);
        var occluderSet = _hasShadows ? _occluderImage!.SampleSet.Set : white.DescriptorSet;
        api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 3, 1, &occluderSet, 0, null);

        var effectSet = resources.EffectSet;
        var pipeline = VkPipeline.Null;
        var lights = lighting.Lights;
        for (var i = 0; i < lights.Length; i++)
        {
            ref readonly var light = ref lights[i];
            var next = pipelines.GetLight(light.Blend);
            if (next != pipeline)
            {
                pipeline = next;
                api.vkCmdBindPipeline(commands, VkPipelineBindPoint.Graphics, pipeline);
            }

            var cookie = light.Cookie.IsNone ? null : textures.Get(light.Cookie.Id);
            var cookieSet = (cookie ?? white).DescriptorSet;
            api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 0, 1, &cookieSet, 0, null);

            var row = _hasShadows ? _shadows.RowOf(i) : -1;
            var rowCoordinate = row < 0 ? -1 : (row + 0.5f) / _shadowImage!.Height;
            var data = LightShaderData.For(light with { Cookie = cookie is null ? default : light.Cookie }, rowCoordinate, settings, pixelsPerUnit);
            var offset = resources.WriteEffectData(data);
            api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 1, 1, &effectSet, 1, &offset);
            api.vkCmdDraw(commands, 4, 1, 0, (uint)(first + i));
        }
    }

    private void DrawEmissive(FrameResources resources, LightingFrame lighting, in Matrix3x2 view, Vector2 mapSize, int first)
    {
        if (lighting.EmissiveBatches.IsEmpty)
            return;
        var api = device.Api;
        var commands = resources.Commands;
        var layout = pipelines.Layout;
        api.vkCmdBindPipeline(commands, VkPipelineBindPoint.Graphics, pipelines.Emissive);
        foreach (ref readonly var batch in lighting.EmissiveBatches)
        {
            if (textures.Get(batch.Texture.Id) is not { } texture)
                continue;
            var set = texture.DescriptorSet;
            api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 0, 1, &set, 0, null);
            var constants = SpriteConstants.For(view, mapSize, texture.InverseSize);
            api.vkCmdPushConstants(commands, layout, VkShaderStageFlags.Vertex, 0, PipelineLibrary.PushConstantsSize, &constants);
            api.vkCmdDraw(commands, 4, (uint)batch.InstanceCount, 0, (uint)(first + batch.FirstInstance));
        }
    }

    private void UploadShadowMap(FrameResources resources, long frameNumber)
    {
        var width = _shadows.Width;
        var rows = _shadows.RowCount;
        if (_shadowImage is null || _shadowImage.Width != width || _shadowImage.Height < rows)
        {
            if (_shadowImage is not null)
                releaser.Release(_shadowImage, frameNumber);
            _shadowImage = new UploadImage(device, descriptors, pipelines.TextureLayout, samplers.Nearest, VkFormat.R16Sfloat, width,
                Math.Max(rows, _shadowImage?.Height * 2 ?? 8));
        }

        _shadowImage.Upload(resources, MemoryMarshal.AsBytes(_shadows.Data), rows, frameNumber);
    }

    private void UploadOccluderMask(FrameResources resources, LightingFrame lighting, in Matrix3x2 worldToMap, int width, int height, long frameNumber)
    {
        if (_occluderImage is null || _occluderImage.Width != width || _occluderImage.Height != height)
        {
            if (_occluderImage is not null)
                releaser.Release(_occluderImage, frameNumber);
            _occluderImage = new UploadImage(device, descriptors, pipelines.TextureLayout, samplers.Nearest, VkFormat.R8Unorm, width, height);
        }

        _occluders.Build(lighting, worldToMap, width, height);
        _occluderImage.Upload(resources, _occluders.Data, height, frameNumber);
    }

    private void EnsureTarget(int width, int height, long frameNumber)
    {
        if (_target is not null && _target.Width == width && _target.Height == height)
            return;
        if (_target is not null)
            releaser.Release(_target, frameNumber);
        _target = new LightMapTarget(device, descriptors, pipelines, samplers.Linear, width, height);
    }

    private void EnsureCapacity(int count)
    {
        if (_instances.Length < count)
            _instances = new SpriteInstance[Math.Max(count, _instances.Length * 2)];
    }

    /// <summary>A light map image with its framebuffer and a set for sampling it.</summary>
    private sealed class LightMapTarget : IDisposable
    {
        private readonly VulkanDevice _device;
        private readonly DescriptorAllocator _descriptors;
        private readonly GpuImage _image;

        public LightMapTarget(VulkanDevice device, DescriptorAllocator descriptors, PipelineLibrary pipelines, VkSampler sampler, int width, int height)
        {
            _device = device;
            _descriptors = descriptors;
            Width = width;
            Height = height;
            _image = new GpuImage(device, width, height, PipelineLibrary.LightMapFormat, VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.Sampled);
            var view = _image.View;
            var framebufferInfo = new VkFramebufferCreateInfo
            {
                renderPass = pipelines.LightPass,
                attachmentCount = 1,
                pAttachments = &view,
                width = (uint)width,
                height = (uint)height,
                layers = 1
            };
            VkFramebuffer framebuffer;
            device.Api.vkCreateFramebuffer(&framebufferInfo, &framebuffer).CheckResult();
            Framebuffer = framebuffer;
            SampleSet = descriptors.Allocate(pipelines.TextureLayout);
            GpuTexture.WriteImageDescriptor(device, SampleSet.Set, view, sampler);
        }

        public int Width { get; }

        public int Height { get; }

        public VkFramebuffer Framebuffer { get; }

        public DescriptorSetAllocation SampleSet { get; }

        public void Dispose()
        {
            _descriptors.Free(SampleSet);
            _device.Api.vkDestroyFramebuffer(Framebuffer);
            _image.Dispose();
        }
    }

    /// <summary>An image the CPU fills every frame, such as the shadow map or the occluder mask, with a set for sampling it unfiltered.</summary>
    private sealed class UploadImage : IDisposable
    {
        private readonly VulkanDevice _device;
        private readonly DescriptorAllocator _descriptors;

        public UploadImage(VulkanDevice device, DescriptorAllocator descriptors, VkDescriptorSetLayout layout, VkSampler sampler, VkFormat format,
            int width, int height)
        {
            _device = device;
            _descriptors = descriptors;
            Width = width;
            Height = height;
            Image = new GpuImage(device, width, height, format, VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst);
            SampleSet = descriptors.Allocate(layout);
            GpuTexture.WriteImageDescriptor(device, SampleSet.Set, Image.View, sampler);
        }

        public int Width { get; }

        public int Height { get; }

        public GpuImage Image { get; }

        public DescriptorSetAllocation SampleSet { get; }

        /// <summary>Records copying <paramref name="rows"/> rows of texels, top first, from <paramref name="data"/> into the image.</summary>
        public void Upload(FrameResources resources, ReadOnlySpan<byte> data, int rows, long frameNumber)
        {
            var span = resources.Staging.Allocate((ulong)data.Length, frameNumber);
            data.CopyTo(new Span<byte>(span.Pointer, data.Length));

            var commands = resources.Commands;
            Image.Transition(commands, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
                VkPipelineStageFlags.FragmentShader, VkAccessFlags.None, VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite);
            var region = new VkBufferImageCopy
            {
                bufferOffset = span.Offset,
                imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
                imageExtent = new VkExtent3D((uint)Width, (uint)rows, 1)
            };
            _device.Api.vkCmdCopyBufferToImage(commands, span.Buffer, Image.Handle, VkImageLayout.TransferDstOptimal, 1, &region);
            Image.Transition(commands, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
                VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite, VkPipelineStageFlags.FragmentShader, VkAccessFlags.ShaderRead);
        }

        public void Dispose()
        {
            _descriptors.Free(SampleSet);
            Image.Dispose();
        }
    }
}
