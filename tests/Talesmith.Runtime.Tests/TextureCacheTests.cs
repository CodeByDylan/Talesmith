using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Events;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Rendering;

namespace Talesmith.Runtime.Tests;

public sealed class TextureCacheTests
{
    [Fact]
    public void AReloadThatChangesTheSizeReplacesAndDestroysTheOldTexture()
    {
        var renderer = new RecordingRenderer();
        var events = new EventBus();
        using var cache = new TextureCache(renderer, new GameSettings(), events);
        var old = new TextureAsset("hero.png", ImageData.Solid(2, 2, Color.White));
        var replacement = new TextureAsset("hero.png", ImageData.Solid(4, 4, Color.White));
        var before = cache.Get(old);
        Texture? replacedBy = null;
        using var subscription = events.Subscribe((ref TextureReplaced e) =>
        {
            Assert.DoesNotContain(e.Old, renderer.Destroyed);
            replacedBy = e.New;
        });

        events.Publish(new AssetReloaded("hero.png", AssetGuid.Empty, typeof(TextureAsset), old, replacement));

        Assert.NotNull(replacedBy);
        Assert.Equal([before], renderer.Destroyed);
        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGetAsset(replacedBy.Value, out var asset));
        Assert.Same(replacement, asset);
        Assert.False(cache.TryGetAsset(before, out _));
    }

    [Fact]
    public void AReloadOfTheSameSizeUpdatesTheTextureInPlace()
    {
        var renderer = new RecordingRenderer();
        var events = new EventBus();
        using var cache = new TextureCache(renderer, new GameSettings(), events);
        var old = new TextureAsset("hero.png", ImageData.Solid(2, 2, Color.White));
        var texture = cache.Get(old);

        events.Publish(new AssetReloaded("hero.png", AssetGuid.Empty, typeof(TextureAsset), old, old with { Image = ImageData.Solid(2, 2, Color.Black) }));

        Assert.Empty(renderer.Destroyed);
        Assert.Equal([texture], renderer.Updated);
    }

    private sealed class RecordingRenderer : IRenderer
    {
        private readonly NullRenderer _inner = new();

        public List<Texture> Destroyed { get; } = [];

        public List<Texture> Updated { get; } = [];

        public RendererInfo Info => _inner.Info;

        public Texture WhiteTexture => _inner.WhiteTexture;

        public Texture CreateTexture(ImageData image, TextureOptions options = default) => _inner.CreateTexture(image, options);

        public void UpdateTexture(Texture texture, ImageData image) => Updated.Add(texture);

        public void DestroyTexture(Texture texture) => Destroyed.Add(texture);

        public void Dispose()
        {
        }
    }
}
