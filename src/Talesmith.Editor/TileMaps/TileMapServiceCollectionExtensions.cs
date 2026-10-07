using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.TileMaps.Dialogs;
using Talesmith.Editor.TileMaps.Panel;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.UI;

namespace Talesmith.Editor.TileMaps;

public static class TileMapServiceCollectionExtensions
{
    /// <summary>The tile map editor: the editing session, map saving, the tile tools, the Tile Map panel and its dialogs.</summary>
    public static IServiceCollection AddEditorTileMaps(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<TileMapDocuments>();
        services.AddSingleton<ICloseGuard>(sp => sp.GetRequiredService<TileMapDocuments>());
        services.AddSingleton<IPlaySessionContributor, TileMapPlayContributor>();
        services.TryAddSingleton<TileMapEditor>();
        services.TryAddSingleton<TileOverlay>();
        services.TryAddSingleton<TileMapDialogs>();
        services.TryAddSingleton<TileMapCommands>();
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<TileMapCommands>());
        services.AddViewportTool<BrushTool>();
        services.AddViewportTool<RandomBrushTool>();
        services.AddViewportTool<EraserTool>();
        services.AddViewportTool<FillTool>();
        services.AddViewportTool<RectangleTool>();
        services.AddViewportTool<LineTool>();
        services.AddViewportTool<CircleTool>();
        services.AddViewportTool<PickerTool>();
        services.AddViewportTool<StampTool>();
        services.AddViewportTool<TerrainTool>();
        services.AddViewportTool<CollisionTool>();
        services.AddViewportTool<TileSelectTool>();
        services.AddViewportTool<ObjectTool>();
        services.AddEditorPanel<TileMapPanel>(new EditorPanelInfo(PanelIds.TileMap, "Tile Map", Icons.Map, DockLocation.Bottom) { Order = 30, Shortcut = "Ctrl+Shift+M" });
        return services;
    }
}
