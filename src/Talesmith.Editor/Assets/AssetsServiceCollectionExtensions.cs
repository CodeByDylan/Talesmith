using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Assets.Browser;
using Talesmith.Editor.Assets.Creation;
using Talesmith.Editor.Assets.Health;
using Talesmith.Editor.Assets.Inspectors;
using Talesmith.Editor.Assets.Opening;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Assets.Previews;
using Talesmith.Editor.Assets.SpriteEditor;
using Talesmith.Editor.Assets.Thumbnails;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.UI;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Assets;

public static class AssetsServiceCollectionExtensions
{
    /// <summary>The Assets panel with thumbnails, asset operations, the Create menu, opening assets and Asset Health.</summary>
    public static IServiceCollection AddEditorAssets(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ThumbnailService>();
        services.AddThumbnailRenderer<TextureThumbnailRenderer>();
        services.AddThumbnailRenderer<FontThumbnailRenderer>();
        services.AddThumbnailRenderer<AudioThumbnailRenderer>();
        services.AddThumbnailRenderer<AtlasThumbnailRenderer>();

        services.TryAddSingleton<AssetOperations>();
        services.TryAddSingleton<AssetHealthLauncher>();
        services.TryAddSingleton<AssetOpener>();
        services.AddAssetOpenHandler<SceneOpenHandler>();
        services.AddAssetOpenHandler<ScriptOpenHandler>();

        services.AddAssetFactory<SceneFactory>();
        services.AddAssetFactory<PrefabFactory>();
        services.AddAssetFactory<TileMapFactory>();
        services.AddAssetFactory<ScriptFactory>();
        services.AddAssetFactory<MaterialFactory>();
        services.AddAssetFactory<SpriteAtlasFactory>();
        services.AddAssetFactory<LocalizationTableFactory>();
        for (var i = 0; i < BuiltInParticlePresets.All.Count; i++)
            services.AddSingleton<IAssetFactory>(new ParticlePresetFactory(BuiltInParticlePresets.All[i], i));

        services.TryAddSingleton<AudioPreviewPlayer>();
        services.AddAssetInspector<TextureInspector>();
        services.AddAssetInspector<AudioInspector>();
        services.AddAssetInspector<FontInspector>();
        services.AddAssetInspector<AtlasInspector>();
        services.AddAssetInspector<DocumentInspector>();
        services.TryAddSingleton<AssetInspectorViewModel>();

        services.TryAddSingleton<SpriteEditorViewModel>();
        services.TryAddSingleton<SpriteEditorService>();
        services.AddSingleton<ICloseGuard>(sp => sp.GetRequiredService<SpriteEditorService>());
        services.AddEditorPanel<SpriteEditorPanel>(new EditorPanelInfo(AssetPanelIds.SpriteEditor, "Sprite Editor", Icons.Cut, DockLocation.Center)
        {
            Order = 2,
            OpenByDefault = false
        });

        services.TryAddSingleton<AssetBrowserViewModel>();
        services.AddEditorCommands<AssetCommands>();
        services.AddEditorPanel<AssetsPanel>(new EditorPanelInfo(PanelIds.Assets, "Assets", Icons.FolderOpen, DockLocation.Bottom) { Shortcut = "Ctrl+Shift+A" });
        return services;
    }
}
