using Talesmith.Imaging;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Owns the renderer's textures: requests from any thread are queued and applied on the render thread before a frame draws.</summary>
internal sealed unsafe class TextureStore(VulkanDevice device, DescriptorAllocator descriptors, VkDescriptorSetLayout layout, Samplers samplers,
    DeferredReleaser releaser) : IDisposable
{
    private readonly Lock _lock = new();
    private List<TextureRequest> _queued = [];
    private List<TextureRequest> _applying = [];
    private GpuTexture?[] _textures = new GpuTexture?[256];
    private int _lastId;

    public Texture Create(ImageData image, TextureOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        var maxSize = (int)device.Limits.maxImageDimension2D;
        if (image.Width > maxSize || image.Height > maxSize)
            throw new ArgumentException($"{image.Width}×{image.Height} exceeds the device's maximum texture size of {maxSize}.", nameof(image));
        lock (_lock)
        {
            var texture = new Texture(++_lastId, image.Width, image.Height);
            _queued.Add(new TextureRequest(TextureRequestKind.Create, texture.Id, image, options));
            return texture;
        }
    }

    public void Update(Texture texture, ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width != texture.Width || image.Height != texture.Height)
            throw new ArgumentException($"The image is {image.Width}×{image.Height} but the texture is {texture.Width}×{texture.Height}.", nameof(image));
        lock (_lock)
            _queued.Add(new TextureRequest(TextureRequestKind.Update, texture.Id, image, default));
    }

    public void Destroy(Texture texture)
    {
        if (texture.IsNone)
            return;
        lock (_lock)
            _queued.Add(new TextureRequest(TextureRequestKind.Destroy, texture.Id, null, default));
    }

    public GpuTexture? Get(int id) => (uint)id < (uint)_textures.Length ? _textures[id] : null;

    /// <summary>Applies queued requests, recording uploads into <paramref name="commands"/>; returns the number of uploads.</summary>
    public int ApplyPending(VkCommandBuffer commands, StagingBuffer staging, long frame)
    {
        lock (_lock)
            (_queued, _applying) = (_applying, _queued);
        var uploads = 0;
        foreach (var request in _applying)
        {
            switch (request.Kind)
            {
                case TextureRequestKind.Create:
                    var image = new GpuImage(device, request.Image!.Width, request.Image.Height, VkFormat.R8G8B8A8Unorm,
                        VkImageUsageFlags.Sampled | VkImageUsageFlags.TransferDst);
                    Store(request.Id, new GpuTexture(device, descriptors, layout, image, samplers.For(request.Options.Filter)));
                    Upload(commands, staging, frame, image, request.Image);
                    uploads++;
                    break;
                case TextureRequestKind.Update when Get(request.Id) is { } texture:
                    Upload(commands, staging, frame, texture.Image, request.Image!);
                    uploads++;
                    break;
                case TextureRequestKind.Destroy when Get(request.Id) is { } texture:
                    _textures[request.Id] = null;
                    releaser.Release(texture, frame);
                    break;
            }
        }

        _applying.Clear();
        return uploads;
    }

    public void Dispose()
    {
        foreach (var texture in _textures)
            texture?.Dispose();
        _textures = [];
    }

    private void Store(int id, GpuTexture texture)
    {
        if (id >= _textures.Length)
            Array.Resize(ref _textures, Math.Max(id + 1, _textures.Length * 2));
        _textures[id] = texture;
    }

    private void Upload(VkCommandBuffer commands, StagingBuffer staging, long frame, GpuImage image, ImageData data)
    {
        var span = staging.Allocate((ulong)data.Pixels.Length, frame);
        data.Pixels.AsSpan().CopyTo(new Span<byte>(span.Pointer, data.Pixels.Length));

        image.Transition(commands, VkImageLayout.Undefined, VkImageLayout.TransferDstOptimal,
            VkPipelineStageFlags.FragmentShader, VkAccessFlags.None, VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite);
        var region = new VkBufferImageCopy
        {
            bufferOffset = span.Offset,
            imageSubresource = new VkImageSubresourceLayers(VkImageAspectFlags.Color, 0, 0, 1),
            imageExtent = new VkExtent3D((uint)data.Width, (uint)data.Height, 1)
        };
        device.Api.vkCmdCopyBufferToImage(commands, span.Buffer, image.Handle, VkImageLayout.TransferDstOptimal, 1, &region);
        image.Transition(commands, VkImageLayout.TransferDstOptimal, VkImageLayout.ShaderReadOnlyOptimal,
            VkPipelineStageFlags.Transfer, VkAccessFlags.TransferWrite, VkPipelineStageFlags.FragmentShader, VkAccessFlags.ShaderRead);
    }

    private enum TextureRequestKind
    {
        Create,
        Update,
        Destroy
    }

    private readonly record struct TextureRequest(TextureRequestKind Kind, int Id, ImageData? Image, TextureOptions Options);
}
