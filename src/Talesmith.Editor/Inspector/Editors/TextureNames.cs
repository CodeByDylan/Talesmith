using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Projects;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>The sprite and animation names of textures, loaded through the edit game in the background.</summary>
internal sealed class TextureNames(IServiceProvider services)
{
    private readonly Dictionary<AssetGuid, TextureAsset?> _loaded = [];

    /// <summary>The names, or none while the texture is still loading.</summary>
    public IReadOnlyList<string> Get(AssetGuid? texture, TextureItemKind kind)
    {
        if (texture is not { } guid)
            return [];
        Prefetch(guid);
        if (_loaded.GetValueOrDefault(guid) is not { } asset)
            return [];
        return kind == TextureItemKind.Animation ? [.. asset.Animations.Select(a => a.Name)] : [.. asset.Sprites.Select(s => s.Name)];
    }

    public void Invalidate() => _loaded.Clear();

    public void Prefetch(AssetGuid guid)
    {
        if (_loaded.ContainsKey(guid))
            return;
        _loaded[guid] = null;
        _ = LoadAsync(guid);
    }

    private async Task LoadAsync(AssetGuid guid)
    {
        var project = services.GetRequiredService<IProjectService>();
        if (project.EditSession?.Game.Services.GetService<IAssetManager>() is not { } assets || !project.Catalog.TryGetPath(guid, out var path))
            return;
        try
        {
            _loaded[guid] = await assets.LoadAsync<TextureAsset>(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _loaded.Remove(guid);
        }
    }
}
