using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Keeps <see cref="SpriteMesh"/> instances in GPU memory, uploading a mesh only when its version changes.</summary>
internal sealed unsafe class MeshCache(VulkanDevice device, DeferredReleaser releaser) : IDisposable
{
    private const int EvictAfterFrames = 300;
    private const int EvictionInterval = 60;

    private readonly Dictionary<int, CachedMesh> _meshes = [];
    private readonly List<int> _unused = [];
    private bool _uploadedThisFrame;

    /// <summary>Gets the buffer holding a mesh batch's instances, recording an upload into <paramref name="commands"/> when it changed.</summary>
    public VkBuffer Prepare(in DrawBatch batch, VkCommandBuffer commands, StagingBuffer staging, long frame)
    {
        if (!_meshes.TryGetValue(batch.MeshId, out var mesh))
        {
            mesh = new CachedMesh();
            _meshes.Add(batch.MeshId, mesh);
        }

        mesh.LastUsedFrame = frame;
        if (mesh.Buffer is null || mesh.Version != batch.MeshVersion)
            Upload(mesh, batch.MeshInstances!, batch.MeshVersion, commands, staging, frame);
        return mesh.Buffer!.Handle;
    }

    /// <summary>Makes this frame's uploads visible to vertex input; call after the last <see cref="Prepare"/> of a frame.</summary>
    public void FinishUploads(VkCommandBuffer commands)
    {
        if (!_uploadedThisFrame)
            return;
        _uploadedThisFrame = false;
        var barrier = new VkMemoryBarrier { srcAccessMask = VkAccessFlags.TransferWrite, dstAccessMask = VkAccessFlags.VertexAttributeRead };
        device.Api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.VertexInput, 0, 1, &barrier, 0, null, 0, null);
    }

    /// <summary>Releases meshes that have not been drawn for a while.</summary>
    public void EvictUnused(long frame)
    {
        if (frame % EvictionInterval != 0)
            return;
        foreach (var (id, mesh) in _meshes)
        {
            if (frame - mesh.LastUsedFrame > EvictAfterFrames)
                _unused.Add(id);
        }

        foreach (var id in _unused)
        {
            if (_meshes.Remove(id, out var mesh) && mesh.Buffer is not null)
                releaser.Release(mesh.Buffer, frame);
        }

        _unused.Clear();
    }

    public void Dispose()
    {
        foreach (var mesh in _meshes.Values)
            mesh.Buffer?.Dispose();
        _meshes.Clear();
    }

    private void Upload(CachedMesh mesh, SpriteInstance[] instances, int version, VkCommandBuffer commands, StagingBuffer staging, long frame)
    {
        var api = device.Api;
        if (!_uploadedThisFrame)
        {
            // Earlier frames may still be reading the buffers about to be overwritten.
            api.vkCmdPipelineBarrier(commands, VkPipelineStageFlags.VertexInput, VkPipelineStageFlags.Transfer, 0, 0, null, 0, null, 0, null);
            _uploadedThisFrame = true;
        }

        var bytes = (ulong)(instances.Length * sizeof(SpriteInstance));
        if (mesh.Buffer is null || mesh.Buffer.Size < bytes)
        {
            if (mesh.Buffer is not null)
                releaser.Release(mesh.Buffer, frame);
            mesh.Buffer = new GpuBuffer(device, bytes, VkBufferUsageFlags.VertexBuffer | VkBufferUsageFlags.TransferDst, BufferMemory.Device);
        }

        var span = staging.Allocate(bytes, frame);
        fixed (SpriteInstance* source = instances)
            Buffer.MemoryCopy(source, span.Pointer, bytes, bytes);
        var region = new VkBufferCopy { srcOffset = span.Offset, dstOffset = 0, size = bytes };
        api.vkCmdCopyBuffer(commands, span.Buffer, mesh.Buffer.Handle, 1, &region);
        mesh.Version = version;
    }

    private sealed class CachedMesh
    {
        public GpuBuffer? Buffer;
        public int Version;
        public long LastUsedFrame;
    }
}
