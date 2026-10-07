using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Events;
using Talesmith.Imaging;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Rendering;

/// <summary>Raised on the game thread when a reloaded texture changed size and is drawn through a new handle from now on.</summary>
/// <remarks>
/// Code holding <see cref="Old"/> switches to <see cref="New"/>; <see cref="Old"/> is destroyed after the handlers ran. Reloads that keep
/// the size update the texture in place instead.
/// </remarks>
public readonly record struct TextureReplaced(string Path, Texture Old, Texture New);

/// <summary>Uploads texture assets to the renderer once and hands out their handles.</summary>
/// <remarks>
/// Textures use the filter from their import settings, or the game's default. When a loaded texture asset is reloaded, its pixels are
/// updated in place if its size is unchanged; otherwise a new texture is created and <see cref="TextureReplaced"/> is published.
/// </remarks>
public sealed class TextureCache : IDisposable
{
    private readonly ConditionalWeakTable<ImageData, StrongBox<Texture>> _byImage = new();
    private readonly Dictionary<int, TextureAsset> _assets = new();
    private readonly List<Texture> _created = [];
    private readonly Lock _lock = new();
    private readonly GameSettings _settings;
    private readonly IEventBus? _events;
    private readonly IDisposable? _reloadSubscription;

    public TextureCache(IRenderer renderer, GameSettings settings, IEventBus? events = null)
    {
        Renderer = renderer;
        _settings = settings;
        _events = events;
        _reloadSubscription = events?.Subscribe<AssetReloaded>(OnAssetReloaded);
    }

    public IRenderer Renderer { get; }

    /// <summary>The number of textures uploaded through this cache.</summary>
    public int Count
    {
        get
        {
            lock (_lock)
                return _created.Count;
        }
    }

    /// <summary>Gets the texture for an asset, uploading it on first use.</summary>
    public Texture Get(TextureAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var texture = Get(asset.Image, asset.Path, asset.Settings.Filter);
        lock (_lock)
            _assets.TryAdd(texture.Id, asset);
        return texture;
    }

    /// <summary>Gets the texture for an image, uploading it on first use.</summary>
    public Texture Get(ImageData image, string? debugName = null) => Get(image, debugName, filter: null);

    /// <summary>Finds the asset a texture was created from with <see cref="Get(TextureAsset)"/>, so it can be saved as a reference.</summary>
    public bool TryGetAsset(Texture texture, [NotNullWhen(true)] out TextureAsset? asset)
    {
        lock (_lock)
            return _assets.TryGetValue(texture.Id, out asset);
    }

    public void Dispose()
    {
        _reloadSubscription?.Dispose();
        lock (_lock)
        {
            foreach (var texture in _created)
                Renderer.DestroyTexture(texture);
            _created.Clear();
            _byImage.Clear();
            _assets.Clear();
        }
    }

    private Texture Get(ImageData image, string? debugName, TextureFilter? filter)
    {
        lock (_lock)
        {
            if (_byImage.TryGetValue(image, out var existing))
                return existing.Value;

            var texture = Renderer.CreateTexture(image, new TextureOptions(filter ?? _settings.TextureFilter, debugName));
            _byImage.Add(image, new StrongBox<Texture>(texture));
            _created.Add(texture);
            return texture;
        }
    }

    private void OnAssetReloaded(ref AssetReloaded e)
    {
        if (e is not { OldAsset: TextureAsset old, NewAsset: TextureAsset replacement })
            return;

        Texture previous;
        lock (_lock)
        {
            if (!_byImage.TryGetValue(old.Image, out var box))
                return;
            previous = box.Value;
            if (old.Width == replacement.Width && old.Height == replacement.Height && old.Settings.Filter == replacement.Settings.Filter)
            {
                Renderer.UpdateTexture(previous, replacement.Image);
                _byImage.AddOrUpdate(replacement.Image, box);
                _assets[previous.Id] = replacement;
                return;
            }
        }

        var texture = Get(replacement);
        _events?.Publish(new TextureReplaced(replacement.Path, previous, texture));
        lock (_lock)
        {
            _byImage.Remove(old.Image);
            _assets.Remove(previous.Id);
            if (!_created.Remove(previous))
                return;
        }

        Renderer.DestroyTexture(previous);
    }
}
