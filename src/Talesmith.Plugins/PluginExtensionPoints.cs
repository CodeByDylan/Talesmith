namespace Talesmith.Plugins;

/// <summary>Well-known extension point ids for the <c>extensions</c> list of a manifest.</summary>
/// <remarks>The list is informational and shown in the editor; plugins may also name extension points of other plugins.</remarks>
public static class PluginExtensionPoints
{
    public const string Systems = "systems";
    public const string Components = "components";
    public const string Scripts = "scripts";
    public const string Scenes = "scenes";
    public const string SceneListeners = "scene.listeners";
    public const string Services = "services";
    public const string Importers = "importers";
    public const string Overlays = "overlays";
    public const string ParticleModules = "particles.modules";
    public const string Lighting = "lighting";
    public const string RenderPasses = "render.passes";
    public const string EditorPanels = "editor.panels";
    public const string EditorCommands = "editor.commands";
    public const string EditorMenus = "editor.menus";
    public const string EditorToolbar = "editor.toolbar";
    public const string EditorInspectors = "editor.inspectors";
    public const string EditorTools = "editor.tools";
    public const string TileTools = "tile.tools";

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        [Systems] = "Systems",
        [Components] = "Components",
        [Scripts] = "Script types",
        [Scenes] = "Scenes",
        [SceneListeners] = "Scene lifecycle hooks",
        [Services] = "Services",
        [Importers] = "Asset importers",
        [Overlays] = "Game overlays",
        [ParticleModules] = "Particle modules",
        [Lighting] = "Lighting features",
        [RenderPasses] = "Render passes",
        [EditorPanels] = "Editor panels",
        [EditorCommands] = "Editor commands",
        [EditorMenus] = "Menu entries",
        [EditorToolbar] = "Toolbar items",
        [EditorInspectors] = "Inspector property editors",
        [EditorTools] = "Viewport tools",
        [TileTools] = "Tile map tools"
    };

    /// <summary>The ids above.</summary>
    public static IReadOnlyCollection<string> Known => DisplayNames.Keys;

    /// <summary>A readable name for the editor, or the id itself for extension points this engine does not know.</summary>
    public static string GetDisplayName(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return DisplayNames.GetValueOrDefault(id, id);
    }
}
