using Talesmith.Imaging;

namespace Talesmith.Rendering;

/// <summary>A renderer that hands out texture handles but draws nothing, for headless simulation and logic-only benchmarks.</summary>
public sealed class NullRenderer : IRenderer
{
    private int _nextId = 1;

    public NullRenderer() => WhiteTexture = new Texture(_nextId++, 1, 1);

    public RendererInfo Info { get; } = new("Null", "None", int.MaxValue, SupportsCustomShaders: false);

    public Texture WhiteTexture { get; }

    public Texture CreateTexture(ImageData image, TextureOptions options = default) =>
        new(Interlocked.Increment(ref _nextId), image.Width, image.Height);

    public void UpdateTexture(Texture texture, ImageData image)
    {
    }

    public void DestroyTexture(Texture texture)
    {
    }

    public void Dispose()
    {
    }
}
