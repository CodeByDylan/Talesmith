using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Selection;

namespace Talesmith.Editor.Assets.Opening;

/// <summary>Opens an asset when it is double-clicked in the Assets panel or chosen in the command palette, such as a scene in the viewport.</summary>
/// <remarks>Register with <see cref="AssetOpenServiceCollectionExtensions.AddAssetOpenHandler{T}"/>; the last registered handler that can open
/// an asset wins, so features and plugins can take over kinds, such as prefabs opening in prefab mode. Assets no handler opens are
/// selected and shown in the asset inspector.</remarks>
public interface IAssetOpenHandler
{
    bool CanOpen(AssetRecord asset);

    Task OpenAsync(AssetRecord asset);
}

public static class AssetOpenServiceCollectionExtensions
{
    public static IServiceCollection AddAssetOpenHandler<T>(this IServiceCollection services)
        where T : class, IAssetOpenHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAssetOpenHandler, T>();
        return services;
    }
}

/// <summary>Opens assets with the registered <see cref="IAssetOpenHandler"/>s.</summary>
public sealed class AssetOpener(IEnumerable<IAssetOpenHandler> handlers, ISelectionService selection, LayoutService layout, EditorPluginGuard plugins)
{
    private readonly IAssetOpenHandler[] _handlers = [.. handlers.Reverse()];

    public async Task OpenAsync(AssetRecord asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        foreach (var handler in _handlers)
        {
            if (plugins.Run(handler, "open an asset", () => handler.CanOpen(asset), false)
                && await plugins.RunAsync(handler, $"open {asset.Path}", () => handler.OpenAsync(asset)))
                return;
        }

        selection.SelectAsset(asset.Guid);
        layout.ShowPanel(PanelIds.Inspector);
    }
}

/// <summary>Scenes open in the viewport.</summary>
public sealed class SceneOpenHandler(ISceneDocumentService documents) : IAssetOpenHandler
{
    public bool CanOpen(AssetRecord asset) => asset.Kind == AssetKind.Scene;

    public Task OpenAsync(AssetRecord asset) => documents.OpenAsync(asset.Path);
}

/// <summary>Scripts open in the code editor from the settings.</summary>
public sealed class ScriptOpenHandler(ConsoleNavigator navigator) : IAssetOpenHandler
{
    public bool CanOpen(AssetRecord asset) => asset.Kind == AssetKind.Script || asset.Kind == AssetKind.Shader;

    public Task OpenAsync(AssetRecord asset)
    {
        navigator.OpenFile(new FileTarget(asset.Path, 1));
        return Task.CompletedTask;
    }
}
