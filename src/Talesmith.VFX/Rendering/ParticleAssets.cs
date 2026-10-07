using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Rendering;
using Talesmith.VFX.Presets;

namespace Talesmith.VFX.Rendering;

/// <summary>A texture and the region of it drawn for each particle.</summary>
public readonly record struct ParticleTextureRegion(Texture Texture, Rect2 Source);

/// <summary>Resolves the textures and presets emitters refer to, loading assets in the background and using fallbacks meanwhile.</summary>
/// <remarks>
/// Asset references are guids, resolved to paths through the <see cref="IAssetCatalog"/> when one is registered. Until a texture has
/// loaded, or when it is missing, emitters draw their built-in texture; until a preset has loaded, emitters use their inline settings.
/// </remarks>
public sealed class ParticleAssets : IDisposable
{
    private static readonly Material Opaque = new(BlendMode.Opaque);

    private readonly IRenderer _renderer;
    private readonly IAssetManager? _assets;
    private readonly IAssetCatalog? _catalog;
    private readonly TextureCache? _textures;
    private readonly ILogger _logger;
    private readonly Texture[] _builtIn = new Texture[Enum.GetValues<BuiltInParticleTexture>().Length];
    private readonly Dictionary<AssetGuid, Task<TextureAsset>?> _textureLoads = [];
    private readonly Dictionary<(AssetGuid, string?), ParticleTextureRegion> _regions = [];
    private readonly Dictionary<AssetGuid, Task<ParticlePreset>?> _presetLoads = [];
    private volatile bool _catalogChanged;

    public ParticleAssets(IRenderer renderer, IServiceProvider services, ILogger<ParticleAssets> logger)
    {
        _renderer = renderer;
        _assets = services.GetService<IAssetManager>();
        _catalog = services.GetService<IAssetCatalog>();
        _textures = services.GetService<TextureCache>();
        _logger = logger;
        if (_catalog is not null)
            _catalog.Changed += (_, _) => _catalogChanged = true;
    }

    /// <summary>The material for a blend mode; the built-in materials are shared so emitters batch with other draws.</summary>
    public static Material MaterialFor(BlendMode blend) => blend switch
    {
        BlendMode.Additive => Material.Additive,
        BlendMode.Multiply => Material.Multiply,
        BlendMode.Opaque => Opaque,
        _ => Material.Default
    };

    /// <summary>Gets an engine-generated texture, uploading it with linear filtering on first use.</summary>
    public ParticleTextureRegion GetBuiltIn(BuiltInParticleTexture kind)
    {
        var index = (int)kind;
        if ((uint)index >= (uint)_builtIn.Length)
            index = 0;
        if (_builtIn[index].IsNone)
        {
            var image = ParticleTextures.Create((BuiltInParticleTexture)index);
            _builtIn[index] = _renderer.CreateTexture(image, new TextureOptions(TextureFilter.Linear, $"Particles/{(BuiltInParticleTexture)index}"));
        }

        var texture = _builtIn[index];
        return new ParticleTextureRegion(texture, new Rect2(0, 0, texture.Width, texture.Height));
    }

    /// <summary>Gets what a renderer module draws: its texture and sprite once loaded, otherwise its built-in texture.</summary>
    public ParticleTextureRegion GetTexture(ParticleRendererModule renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        if (renderer.Texture.IsEmpty)
            return GetBuiltIn(renderer.BuiltInTexture);
        ForgetIfCatalogChanged();
        var key = (renderer.Texture, string.IsNullOrEmpty(renderer.Sprite) ? null : renderer.Sprite);
        if (_regions.TryGetValue(key, out var region))
            return region;
        if (LoadTexture(renderer.Texture) is not { } asset || _textures is null)
            return GetBuiltIn(renderer.BuiltInTexture);

        var texture = _textures.Get(asset);
        var source = key.Item2 is { } sprite && asset.FindSprite(sprite) is { } slice ? slice.Rect : new Rect2(0, 0, asset.Width, asset.Height);
        region = new ParticleTextureRegion(texture, source);
        _regions[key] = region;
        return region;
    }

    /// <summary>Gets a preset's settings once loaded, or null while loading or when it is missing.</summary>
    public ParticleSettings? GetPreset(AssetGuid guid)
    {
        if (guid.IsEmpty || _assets is null || _catalog is null)
            return null;
        ForgetIfCatalogChanged();
        if (!_presetLoads.TryGetValue(guid, out var load))
        {
            load = _catalog.TryGetPath(guid, out var path) ? _assets.LoadAsync<ParticlePreset>(path) : null;
            if (load is null)
                ParticleLog.PresetMissing(_logger, guid.ToString());
            _presetLoads[guid] = load;
        }

        return Completed(_presetLoads, load, guid)?.Settings;
    }

    public void Dispose()
    {
        foreach (var texture in _builtIn)
        {
            if (!texture.IsNone)
                _renderer.DestroyTexture(texture);
        }

        Array.Clear(_builtIn);
    }

    /// <summary>Forgets resolved references after assets were moved, renamed or removed, so they resolve again.</summary>
    private void ForgetIfCatalogChanged()
    {
        if (!_catalogChanged)
            return;
        _catalogChanged = false;
        _textureLoads.Clear();
        _regions.Clear();
        _presetLoads.Clear();
    }

    private TextureAsset? LoadTexture(AssetGuid guid)
    {
        if (_assets is null || _catalog is null)
            return null;
        if (!_textureLoads.TryGetValue(guid, out var load))
        {
            load = _catalog.TryGetPath(guid, out var path) ? _assets.LoadAsync<TextureAsset>(path) : null;
            if (load is null)
                ParticleLog.TextureMissing(_logger, guid.ToString());
            _textureLoads[guid] = load;
        }

        return Completed(_textureLoads, load, guid);
    }

    /// <summary>Gets a finished load's result; a failed load is logged once and forgotten so the fallback is used from then on.</summary>
    private T? Completed<T>(Dictionary<AssetGuid, Task<T>?> loads, Task<T>? load, AssetGuid guid) where T : class
    {
        if (load is null || !load.IsCompleted)
            return null;
        if (load.IsCompletedSuccessfully)
            return load.Result;
        ParticleLog.AssetFailed(_logger, load.Exception?.GetBaseException(), guid.ToString());
        loads[guid] = null;
        return null;
    }
}
