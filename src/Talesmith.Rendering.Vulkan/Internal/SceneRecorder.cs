using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.Mathematics;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Records a <see cref="RenderFrame"/>: uploads, the light map, the batches in order with the light map composited over the lit ones, post effects and the copy into a readback buffer.</summary>
internal sealed unsafe class SceneRecorder(VulkanDevice device, PipelineLibrary pipelines, TextureStore textures, MeshCache meshes, LightMapRecorder lighting)
{
    private VkBuffer[] _meshBuffers = new VkBuffer[64];

    /// <summary>Records the frame into the scene target and through its post effects.</summary>
    /// <returns>The index of the target holding the finished image.</returns>
    public int Record(FrameResources resources, RenderFrame frame, RenderTargets targets, long frameNumber, out RenderStats stats)
    {
        var commands = resources.Commands;
        var batches = frame.Batches;
        lighting.Prepare(frame);
        var effectBlocks = PrepareBatches(resources, batches, frameNumber) + frame.PostEffects.Count + lighting.EffectBlocks;
        resources.ReserveEffectData(effectBlocks);
        resources.UploadInstances(frame.Instances, lighting.Instances);
        if (lighting.IsActive)
            lighting.Record(resources, frame, targets.Width, targets.Height, frame.Instances.Length, frameNumber);

        var targetSize = new Vector2(targets.Width, targets.Height);
        BeginPass(commands, pipelines.ScenePass, targets.Framebuffer(0), targets, frame.ClearColor);
        int drawCalls;
        if (lighting.IsActive)
        {
            var firstUnlit = frame.Lighting.FirstUnlitBatch(batches);
            drawCalls = DrawBatches(resources, frame, batches, 0, firstUnlit, targetSize);
            Composite(commands);
            drawCalls += 1 + DrawBatches(resources, frame, batches, firstUnlit, batches.Length, targetSize);
        }
        else
        {
            drawCalls = DrawBatches(resources, frame, batches, 0, batches.Length, targetSize);
        }

        device.Api.vkCmdEndRenderPass(commands);

        var source = 0;
        var postEffects = 0;
        foreach (var effect in frame.PostEffects)
        {
            var pipeline = pipelines.GetPostEffect(effect.Shader);
            if (pipeline == VkPipeline.Null)
                continue;
            ApplyPostEffect(resources, targets, source, pipeline, new EffectData(effect.P0, effect.P1, effect.P2, effect.P3, targetSize, (float)frame.Time));
            source = 1 - source;
            postEffects++;
            drawCalls++;
        }

        stats = new RenderStats(drawCalls, batches.Length, frame.SpriteCount, frame.MeshInstanceCount, postEffects);
        return source;
    }

    /// <summary>Records a copy of a finished target into its place in a readback buffer the CPU can read once the frame completes.</summary>
    /// <param name="fillBorders">Fills the buffer with the border color first, for the pixels outside the view.</param>
    public void CopyToReadback(VkCommandBuffer commands, GpuImage image, GpuBuffer readback, in ViewPlacement placement, bool fillBorders)
    {
        image.Transition(commands, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal,
            VkPipelineStageFlags.ColorAttachmentOutput, VkAccessFlags.ColorAttachmentWrite, VkPipelineStageFlags.Transfer, VkAccessFlags.TransferRead);
        if (fillBorders)
            FillBorders(commands, readback, placement);
        var region = new VkBufferImageCopy
        {
            bufferOffset = ((ulong)placement.Y * (ulong)placement.OutputWidth + (ulong)placement.X) * 4,
            bufferRowLength = (uint)placement.OutputWidth,
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            imageExtent = new VkExtent3D((uint)image.Width, (uint)image.Height, 1)
        };
        device.Api.vkCmdCopyImageToBuffer(commands, image.Handle, VkImageLayout.TransferSrcOptimal, readback.Handle, 1, &region);
        var barrier = new VkBufferMemoryBarrier
        {
            srcAccessMask = VkAccessFlags.TransferWrite,
            dstAccessMask = VkAccessFlags.HostRead,
            srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            buffer = readback.Handle,
            offset = 0,
            size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE
        };
        device.Api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.Host, 0, 0, null, 1, &barrier, 0, null);
    }

    /// <summary>
    /// Records a blit of a finished target into its place in a shared image, converting BGRA to RGBA, with the border color around it, and
    /// hands the image to the external API in transfer-source layout.
    /// </summary>
    /// <param name="fromExternal">Whether the external API owns the image from a previous frame and must hand it back first.</param>
    public void CopyToShared(VkCommandBuffer commands, GpuImage image, SharedImageResources target, in ViewPlacement placement, bool fromExternal)
    {
        image.Transition(commands, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.TransferSrcOptimal,
            VkPipelineStageFlags.ColorAttachmentOutput, VkAccessFlags.ColorAttachmentWrite, VkPipelineStageFlags.Transfer, VkAccessFlags.TransferRead);

        var acquire = SharedBarrier(target.Image, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal, VkAccessFlags.None, VkAccessFlags.TransferWrite,
            fromExternal ? Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_EXTERNAL : Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            fromExternal ? device.QueueFamily : Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED);
        device.Api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.TopOfPipe, VkPipelineStageFlags.Transfer, 0, 0, null, 0, null, 1, &acquire);
        if (placement.HasBorders)
            ClearBorders(commands, target.Image, placement.Border);

        var blit = new VkImageBlit
        {
            srcSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            dstSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1)
        };
        blit.srcOffsets[1] = new VkOffset3D(image.Width, image.Height, 1);
        blit.dstOffsets[0] = new VkOffset3D(placement.X, placement.Y, 0);
        blit.dstOffsets[1] = new VkOffset3D(placement.X + placement.Width, placement.Y + placement.Height, 1);
        device.Api.vkCmdBlitImage(commands, image.Handle, VkImageLayout.TransferSrcOptimal, target.Image, VkImageLayout.TransferDstOptimal, 1, &blit,
            VkFilter.Nearest);

        var release = SharedBarrier(target.Image, VkImageLayout.TransferDstOptimal, VkImageLayout.TransferSrcOptimal, VkAccessFlags.TransferWrite,
            VkAccessFlags.None, device.QueueFamily, Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_EXTERNAL);
        device.Api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.BottomOfPipe, 0, 0, null, 0, null, 1, &release);
    }

    private void FillBorders(VkCommandBuffer commands, GpuBuffer readback, in ViewPlacement placement)
    {
        device.Api.vkCmdFillBuffer(commands, readback.Handle, 0, Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE, placement.BorderBgra);
        var barrier = new VkBufferMemoryBarrier
        {
            srcAccessMask = VkAccessFlags.TransferWrite,
            dstAccessMask = VkAccessFlags.TransferWrite,
            srcQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED,
            buffer = readback.Handle,
            offset = 0,
            size = Vortice.Vulkan.Vulkan.VK_WHOLE_SIZE
        };
        device.Api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.Transfer, 0, 0, null, 1, &barrier, 0, null);
    }

    /// <summary>Clears a shared image in transfer-destination layout to the border color before the view is blitted into it.</summary>
    private void ClearBorders(VkCommandBuffer commands, VkImage image, Color border)
    {
        var alpha = border.A / 255f;
        var color = new VkClearColorValue(border.R / 255f * alpha, border.G / 255f * alpha, border.B / 255f * alpha, alpha);
        var range = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1);
        device.Api.vkCmdClearColorImage(commands, image, VkImageLayout.TransferDstOptimal, &color, 1, &range);
        var barrier = SharedBarrier(image, VkImageLayout.TransferDstOptimal, VkImageLayout.TransferDstOptimal, VkAccessFlags.TransferWrite,
            VkAccessFlags.TransferWrite, Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED, Vortice.Vulkan.Vulkan.VK_QUEUE_FAMILY_IGNORED);
        device.Api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.Transfer, 0, 0, null, 0, null, 1, &barrier);
    }

    private static VkImageMemoryBarrier SharedBarrier(VkImage image, VkImageLayout from, VkImageLayout to, VkAccessFlags sourceAccess,
        VkAccessFlags destinationAccess, uint sourceFamily, uint destinationFamily) => new()
        {
            srcAccessMask = sourceAccess,
            dstAccessMask = destinationAccess,
            oldLayout = from,
            newLayout = to,
            srcQueueFamilyIndex = sourceFamily,
            dstQueueFamilyIndex = destinationFamily,
            image = image,
            subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1)
        };

    /// <summary>Uploads changed meshes and counts the batches that need Effect data.</summary>
    private int PrepareBatches(FrameResources resources, ReadOnlySpan<DrawBatch> batches, long frameNumber)
    {
        if (_meshBuffers.Length < batches.Length)
            _meshBuffers = new VkBuffer[Math.Max(batches.Length, _meshBuffers.Length * 2)];
        var effectBatches = 0;
        for (var i = 0; i < batches.Length; i++)
        {
            ref readonly var batch = ref batches[i];
            if (batch.Material.Shader is not null)
                effectBatches++;
            if (!batch.IsMesh)
                continue;
            _meshBuffers[i] = meshes.Prepare(batch, resources.Commands, resources.Staging, frameNumber);
        }

        meshes.FinishUploads(resources.Commands);
        return effectBatches;
    }

    private void Composite(VkCommandBuffer commands)
    {
        var api = device.Api;
        api.vkCmdBindPipeline(commands, VkPipelineBindPoint.Graphics, pipelines.Composite);
        var set = lighting.LightMapSet;
        api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, pipelines.Layout, 0, 1, &set, 0, null);
        api.vkCmdDraw(commands, 3, 1, 0, 0);
    }

    private int DrawBatches(FrameResources resources, RenderFrame frame, ReadOnlySpan<DrawBatch> batches, int start, int end, Vector2 targetSize)
    {
        var api = device.Api;
        var commands = resources.Commands;
        var layout = pipelines.Layout;
        var effectSet = resources.EffectSet;
        var drawCalls = 0;
        var pipeline = VkPipeline.Null;
        var textureSet = VkDescriptorSet.Null;
        var vertexBuffer = VkBuffer.Null;
        Material? material = null;
        Material? effectMaterial = null;
        var entry = default(PipelineEntry);
        var constants = default(SpriteConstants);
        var constantsValid = false;

        for (var i = start; i < end; i++)
        {
            ref readonly var batch = ref batches[i];
            if (textures.Get(batch.Texture.Id) is not { } texture)
                continue;

            if (!ReferenceEquals(batch.Material, material))
            {
                material = batch.Material;
                entry = pipelines.GetSprite(material.Blend, material.Shader);
            }

            if (entry.Pipeline != pipeline)
            {
                pipeline = entry.Pipeline;
                api.vkCmdBindPipeline(commands, VkPipelineBindPoint.Graphics, pipeline);
            }

            if (texture.DescriptorSet != textureSet)
            {
                textureSet = texture.DescriptorSet;
                api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 0, 1, &textureSet, 0, null);
            }

            if (entry.UsesEffectData && !ReferenceEquals(material, effectMaterial))
            {
                effectMaterial = material;
                var offset = resources.WriteEffectData(new EffectData(material.Parameters, targetSize, (float)frame.Time));
                api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, layout, 1, 1, &effectSet, 1, &offset);
            }

            var next = SpriteConstants.For(batch.Space == RenderSpace.World ? frame.ViewMatrix : frame.ScreenMatrix, targetSize, texture.InverseSize);
            if (!constantsValid || !next.Equals(constants))
            {
                constants = next;
                constantsValid = true;
                api.vkCmdPushConstants(commands, layout, VkShaderStageFlags.Vertex, 0, PipelineLibrary.PushConstantsSize, &next);
            }

            var buffer = batch.IsMesh ? _meshBuffers[i] : resources.InstanceBuffer;
            if (buffer != vertexBuffer)
            {
                vertexBuffer = buffer;
                ulong bufferOffset = 0;
                api.vkCmdBindVertexBuffers(commands, 0, 1, &buffer, &bufferOffset);
            }

            api.vkCmdDraw(commands, 4, (uint)batch.InstanceCount, 0, batch.IsMesh ? 0u : (uint)batch.FirstInstance);
            drawCalls++;
        }

        return drawCalls;
    }

    private void ApplyPostEffect(FrameResources resources, RenderTargets targets, int source, VkPipeline pipeline, in EffectData data)
    {
        var api = device.Api;
        var commands = resources.Commands;
        targets.Image(source).Transition(commands, VkImageLayout.ColorAttachmentOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags.ColorAttachmentOutput, VkAccessFlags.ColorAttachmentWrite, VkPipelineStageFlags.FragmentShader, VkAccessFlags.ShaderRead);
        BeginPass(commands, pipelines.EffectPass, targets.Framebuffer(1 - source), targets, default);
        api.vkCmdBindPipeline(commands, VkPipelineBindPoint.Graphics, pipeline);
        var sceneSet = targets.SampleSet(source);
        api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, pipelines.Layout, 0, 1, &sceneSet, 0, null);
        var effectSet = resources.EffectSet;
        var offset = resources.WriteEffectData(data);
        api.vkCmdBindDescriptorSets(commands, VkPipelineBindPoint.Graphics, pipelines.Layout, 1, 1, &effectSet, 1, &offset);
        api.vkCmdDraw(commands, 3, 1, 0, 0);
        api.vkCmdEndRenderPass(commands);
    }

    private void BeginPass(VkCommandBuffer commands, VkRenderPass renderPass, VkFramebuffer framebuffer, RenderTargets targets, Color clearColor)
    {
        var api = device.Api;
        var extent = new VkExtent2D((uint)targets.Width, (uint)targets.Height);
        var alpha = clearColor.A / 255f;
        var clear = new VkClearValue(clearColor.R / 255f * alpha, clearColor.G / 255f * alpha, clearColor.B / 255f * alpha, alpha);
        var beginInfo = new VkRenderPassBeginInfo
        {
            renderPass = renderPass,
            framebuffer = framebuffer,
            renderArea = new VkRect2D(0, 0, extent.width, extent.height),
            clearValueCount = 1,
            pClearValues = &clear
        };
        api.vkCmdBeginRenderPass(commands, &beginInfo, VkSubpassContents.Inline);
        var viewport = new VkViewport { x = 0, y = 0, width = targets.Width, height = targets.Height, minDepth = 0, maxDepth = 1 };
        var scissor = new VkRect2D(0, 0, extent.width, extent.height);
        api.vkCmdSetViewport(commands, 0, 1, &viewport);
        api.vkCmdSetScissor(commands, 0, 1, &scissor);
    }
}

/// <summary>The sprite vertex shader's push constants.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct SpriteConstants(Vector4 ViewLinear, Vector2 ViewTranslation, Vector2 TargetSize, Vector2 InverseTextureSize)
{
    public static SpriteConstants For(in Matrix3x2 view, Vector2 targetSize, Vector2 inverseTextureSize) =>
        new(new Vector4(view.M11, view.M12, view.M21, view.M22), view.Translation, targetSize, inverseTextureSize);
}
