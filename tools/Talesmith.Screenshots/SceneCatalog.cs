using Talesmith.Screenshots.Capture;
using Talesmith.Screenshots.Scenes;

namespace Talesmith.Screenshots;

/// <summary>Every scene the tool renders, in order. Add new scenes here.</summary>
internal static class SceneCatalog
{
    public static IReadOnlyList<ScreenshotScene> All { get; } =
    [
        new ToolkitGalleryScene(),
        new IconGalleryScene(),
        new EditorControlsScene(),
        new PropertyGridScene(),
        new DockWorkspaceScene(),
        new DockDragScene(),
        new DockRearrangedScene(),
        new HubScene(),
        new HubNewProjectScene(),
        new EditorWindowScene(),
        new EditorHexScene(),
        new EditorSampleScene(),
        new EditorPlayScene(),
        new EditorPaletteScene(),
        new EditorSettingsScene(),
        new EditorConsoleScene(),
        new ParticleEditorScene(),
        new ParticlePresetsScene(),
        new LightingPanelScene(),
        new AssetsGridScene(),
        new AssetsListScene(),
        new AssetTextureInspectorScene(),
        new AssetAudioInspectorScene(),
        new SpriteEditorScene(),
        new AssetsCreateMenuScene(),
        new AssetInspectorScene("editor-asset-atlas", "sprites/characters.tatlas"),
        new AssetInspectorScene("editor-asset-font", "fonts/Inter-Bold.ttf"),
        new AssetInspectorScene("editor-asset-prefab", "prefabs/crab.tprefab"),
        new TileMapScene(),
        new TileMapSquareScene(),
        new TileMapStrokeScene(),
        new TilesetImportScene(),
        new TerrainEditorScene(),
        new EditorHierarchyScene(),
        new EditorInspectorScene(),
        new EditorAddComponentScene(),
        new EditorGizmoScene(),
        new EditorPrefabScene(),
        new EditorPlayInspectorScene(),
        new EditorBuildProgressScene(),
        new EditorBuildReportScene(),
        new EditorPluginsScene(),
        new EditorInputSettingsScene(),
        new EditorPhysicsSettingsScene(),
        new EditorScriptErrorScene(),
    ];
}
