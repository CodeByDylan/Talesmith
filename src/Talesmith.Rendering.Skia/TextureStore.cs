using SkiaSharp;
using Talesmith.Imaging;

namespace Talesmith.Rendering.Skia;

/// <summary>An immutable texture image with the shader that samples it; updating a texture replaces its slot.</summary>
internal sealed class TextureSlot(SKImage image, SKShader shader, TextureFilter filter) : IDisposable
{
    public SKImage Image { get; } = image;

    /// <summary>Samples the image in pixel coordinates with the texture's filter.</summary>
    public SKShader Shader { get; } = shader;

    public TextureFilter Filter { get; } = filter;

    public void Dispose()
    {
        Shader.Dispose();
        Image.Dispose();
    }
}

/// <summary>Thread-safe texture storage; images replaced or destroyed during a frame are disposed when the frame ends.</summary>
internal sealed class TextureStore(SKSamplingOptions linearSampling) : IDisposable
{
    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest);

    private readonly Lock _lock = new();
    private readonly Dictionary<int, TextureSlot> _slots = [];
    private readonly List<TextureSlot> _retired = [];
    private int _nextId;
    private int _uploads;
    private bool _rendering;

    public Texture Create(ImageData image, TextureFilter filter)
    {
        ArgumentNullException.ThrowIfNull(image);
        var slot = CreateSlot(image, filter);
        lock (_lock)
        {
            var id = ++_nextId;
            _slots.Add(id, slot);
            _uploads++;
            return new Texture(id, image.Width, image.Height);
        }
    }

    public void Update(Texture texture, ImageData image)
    {
        ArgumentNullException.ThrowIfNull(image);
        TextureFilter filter;
        lock (_lock)
        {
            if (!_slots.TryGetValue(texture.Id, out var current))
                throw new ArgumentException($"Texture {texture.Id} does not exist.", nameof(texture));
            if (image.Width != current.Image.Width || image.Height != current.Image.Height)
                throw new ArgumentException($"Expected a {current.Image.Width}×{current.Image.Height} image, got {image.Width}×{image.Height}.", nameof(image));
            filter = current.Filter;
        }

        var slot = CreateSlot(image, filter);
        lock (_lock)
        {
            if (!_slots.TryGetValue(texture.Id, out var current))
            {
                slot.Dispose();
                return;
            }

            _slots[texture.Id] = slot;
            _uploads++;
            Retire(current);
        }
    }

    public void Destroy(Texture texture)
    {
        lock (_lock)
        {
            if (_slots.Remove(texture.Id, out var slot))
                Retire(slot);
        }
    }

    /// <summary>Gets the current slot of a texture, or null when it does not exist.</summary>
    public TextureSlot? Find(int id)
    {
        lock (_lock)
            return _slots.GetValueOrDefault(id);
    }

    /// <summary>Marks a frame as rendering and returns the number of textures created or updated since the previous frame.</summary>
    public int BeginFrame()
    {
        lock (_lock)
        {
            _rendering = true;
            var uploads = _uploads;
            _uploads = 0;
            return uploads;
        }
    }

    public void EndFrame()
    {
        lock (_lock)
        {
            _rendering = false;
            foreach (var slot in _retired)
                slot.Dispose();
            _retired.Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var slot in _slots.Values)
                slot.Dispose();
            foreach (var slot in _retired)
                slot.Dispose();
            _slots.Clear();
            _retired.Clear();
        }
    }

    private void Retire(TextureSlot slot)
    {
        if (_rendering)
            _retired.Add(slot);
        else
            slot.Dispose();
    }

    private TextureSlot CreateSlot(ImageData image, TextureFilter filter)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var skImage = SKImage.FromPixelCopy(info, image.Pixels.AsSpan(), image.Stride)
            ?? throw new InvalidOperationException($"Skia could not create a {image.Width}×{image.Height} image.");
        var sampling = filter == TextureFilter.Nearest ? NearestSampling : linearSampling;
        return new TextureSlot(skImage, skImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling), filter);
    }
}
