using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Everything one frame in flight records into and reads from, reused once the GPU has finished that frame.</summary>
internal sealed unsafe class FrameResources : IDisposable
{
    private const uint EffectDataSize = 80;

    private readonly VulkanDevice _device;
    private readonly DescriptorAllocator _descriptors;
    private readonly DescriptorSetAllocation _effectSet;
    private readonly VkCommandPool _commandPool;
    private readonly VkQueryPool _queryPool;
    private readonly uint _effectStride;
    private GpuBuffer? _instances;
    private GpuBuffer? _effects;
    private uint _effectsUsed;

    public FrameResources(VulkanDevice device, DescriptorAllocator descriptors, VkDescriptorSetLayout effectLayout, DeferredReleaser releaser,
        bool measureGpuTime)
    {
        _device = device;
        _descriptors = descriptors;
        var api = device.Api;
        var poolInfo = new VkCommandPoolCreateInfo { flags = VkCommandPoolCreateFlags.Transient, queueFamilyIndex = device.QueueFamily };
        VkCommandPool pool;
        api.vkCreateCommandPool(&poolInfo, &pool).CheckResult();
        _commandPool = pool;

        var allocateInfo = new VkCommandBufferAllocateInfo { commandPool = pool, level = VkCommandBufferLevel.Primary, commandBufferCount = 1 };
        VkCommandBuffer commands;
        api.vkAllocateCommandBuffers(&allocateInfo, &commands).CheckResult();
        Commands = commands;

        var fenceInfo = new VkFenceCreateInfo();
        VkFence fence;
        api.vkCreateFence(&fenceInfo, &fence).CheckResult();
        Fence = fence;

        if (measureGpuTime)
        {
            var queryInfo = new VkQueryPoolCreateInfo { queryType = VkQueryType.Timestamp, queryCount = 2 };
            VkQueryPool queryPool;
            api.vkCreateQueryPool(&queryInfo, &queryPool).CheckResult();
            _queryPool = queryPool;
        }

        var alignment = (uint)device.Limits.minUniformBufferOffsetAlignment;
        _effectStride = (EffectDataSize + alignment - 1) / alignment * alignment;
        _effectSet = descriptors.Allocate(effectLayout);
        Staging = new StagingBuffer(device, releaser);
    }

    public VkCommandBuffer Commands { get; }

    public VkFence Fence { get; }

    public StagingBuffer Staging { get; }

    /// <summary>The frame number last submitted with these resources, or -1.</summary>
    public long SubmittedFrame { get; set; } = -1;

    /// <summary>Instances of the frame's non-mesh batches, in batch order.</summary>
    public VkBuffer InstanceBuffer => _instances?.Handle ?? VkBuffer.Null;

    /// <summary>Set 1 for this frame, pointing at its Effect uniform data with a dynamic offset.</summary>
    public VkDescriptorSet EffectSet => _effectSet.Set;

    /// <summary>Readies the resources for recording; the previous submission must have completed.</summary>
    public void BeginRecording()
    {
        var api = _device.Api;
        var fence = Fence;
        api.vkResetFences(1, &fence).CheckResult();
        SubmittedFrame = -1;
        api.vkResetCommandPool(_commandPool, 0).CheckResult();
        Staging.Reset();
        _effectsUsed = 0;
        var beginInfo = new VkCommandBufferBeginInfo { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        api.vkBeginCommandBuffer(Commands, &beginInfo).CheckResult();
        if (_queryPool != VkQueryPool.Null)
        {
            api.vkCmdResetQueryPool(Commands, _queryPool, 0, 2);
            api.vkCmdWriteTimestamp(Commands, VkPipelineStageFlags.TopOfPipe, _queryPool, 0);
        }
    }

    public void EndRecording()
    {
        if (_queryPool != VkQueryPool.Null)
            _device.Api.vkCmdWriteTimestamp(Commands, VkPipelineStageFlags.BottomOfPipe, _queryPool, 1);
        _device.Api.vkEndCommandBuffer(Commands).CheckResult();
    }

    /// <summary>Whether the last submission has finished, without waiting.</summary>
    public bool IsComplete => SubmittedFrame < 0 || _device.Api.vkGetFenceStatus(Fence) == VkResult.Success;

    public void WaitForCompletion()
    {
        if (SubmittedFrame < 0)
            return;
        var fence = Fence;
        _device.Api.vkWaitForFences(1, &fence, true, ulong.MaxValue).CheckResult();
    }

    /// <summary>Reads how long the GPU spent on the last completed submission; call only after its fence has signaled.</summary>
    public bool TryReadGpuMilliseconds(out double milliseconds)
    {
        milliseconds = 0;
        if (_queryPool == VkQueryPool.Null || SubmittedFrame < 0)
            return false;
        var timestamps = stackalloc ulong[2];
        var result = _device.Api.vkGetQueryPoolResults(_queryPool, 0, 2, sizeof(ulong) * 2, timestamps, sizeof(ulong), VkQueryResultFlags.Bit64);
        if (result != VkResult.Success)
            return false;
        milliseconds = (timestamps[1] - timestamps[0]) * (double)_device.Limits.timestampPeriod / 1_000_000;
        return true;
    }

    /// <summary>Copies the frame's instances, followed by its lighting instances, into its instance buffer, growing the buffer when needed.</summary>
    public void UploadInstances(ReadOnlySpan<SpriteInstance> instances, ReadOnlySpan<SpriteInstance> lighting)
    {
        var sceneBytes = (ulong)(instances.Length * sizeof(SpriteInstance));
        var bytes = sceneBytes + (ulong)(lighting.Length * sizeof(SpriteInstance));
        if (bytes == 0)
            return;
        if (_instances is null || _instances.Size < bytes)
        {
            _instances?.Dispose();
            _instances = new GpuBuffer(_device, Math.Max(bytes * 3 / 2, 64 * 1024), VkBufferUsageFlags.VertexBuffer, BufferMemory.Upload);
        }

        fixed (SpriteInstance* source = instances)
            Buffer.MemoryCopy(source, _instances.Mapped, _instances.Size, sceneBytes);
        fixed (SpriteInstance* source = lighting)
            Buffer.MemoryCopy(source, _instances.Mapped + sceneBytes, _instances.Size - sceneBytes, bytes - sceneBytes);
    }

    /// <summary>Makes room for <paramref name="count"/> Effect blocks this frame; call before any are written.</summary>
    public void ReserveEffectData(int count)
    {
        var bytes = (ulong)(count * _effectStride);
        if (bytes == 0 || (_effects is not null && _effects.Size >= bytes))
            return;
        _effects?.Dispose();
        _effects = new GpuBuffer(_device, Math.Max(bytes * 3 / 2, _effectStride * 64ul), VkBufferUsageFlags.UniformBuffer, BufferMemory.Upload);

        var bufferInfo = new VkDescriptorBufferInfo { buffer = _effects.Handle, offset = 0, range = EffectDataSize };
        var write = new VkWriteDescriptorSet
        {
            dstSet = _effectSet.Set,
            dstBinding = 0,
            descriptorCount = 1,
            descriptorType = VkDescriptorType.UniformBufferDynamic,
            pBufferInfo = &bufferInfo
        };
        _device.Api.vkUpdateDescriptorSets(1, &write, 0, null);
    }

    /// <summary>Writes one std140 block of at most 80 bytes, such as <see cref="EffectData"/>, and returns its dynamic offset.</summary>
    public uint WriteEffectData<T>(in T data) where T : unmanaged
    {
        System.Diagnostics.Debug.Assert(sizeof(T) <= EffectDataSize);
        var offset = _effectsUsed * _effectStride;
        *(T*)(_effects!.Mapped + offset) = data;
        _effectsUsed++;
        return offset;
    }

    public void Dispose()
    {
        var api = _device.Api;
        Staging.Dispose();
        _instances?.Dispose();
        _effects?.Dispose();
        _descriptors.Free(_effectSet);
        if (_queryPool != VkQueryPool.Null)
            api.vkDestroyQueryPool(_queryPool);
        api.vkDestroyFence(Fence);
        api.vkDestroyCommandPool(_commandPool);
    }
}
