using Talesmith.Screenshots.Scenes;

namespace Talesmith.Screenshots.Docs;

/// <summary>The screenshots of the plugins section, in website/static/img/screenshots/plugins.</summary>
internal static class PluginsShots
{
    public static IReadOnlyList<DocsShot> All { get; } =
    [
        new("plugins-panel", new PluginsPanelDocsScene(), "The editor with the Plugins panel open on Hex Quest: cards for the cutscene and gameplay plugins, the cutscene card expanded to show its permission and extension points"),
        new("new-plugin-dialog", new NewPluginDialogScene(), "The New plugin dialog with Spinners typed as the plugin name and a Create button"),
        new("restart-required", new RestartRequiredScene(), "The Plugins panel after a rescan: a Restart required bar with a Reload now button above the Spinners card, which shows Not loaded"),
        new("spinner-inspector", new SpinnerInspectorScene(), "The inspector of Coin 9 with a Spinner section whose Speed slider is set to 90"),
        new("plugin-problems", new PluginProblemsScene(), "Plugin cards in the Plugins panel: a folder with an invalid manifest, Map notes with editor errors, Quest log skipped for a missing dependency, Weather with a red File system access chip, and Photo mode switched off"),
        new("plugin-details", new PluginDetailsScene(), "The Spinners card expanded, with its Runtime scene access and Editor UI access permissions, the extension points it lists and its saved settings speedScale and reverseAll"),
        new("editor-plugin", new EditorPluginScene(), "The editor with the Spinners editor plugin: the Spinners panel listing three coins, the Place spinner tool selected with its Speed option, an arc gizmo with a handle around the selected coin, and an Add spinner button in the app bar"),
        new("cutscene-dialogue", new CutsceneDialogueScene(), "Hex Quest in play mode with the dialogue box of the cutscene plugin: Harbormaster Wren asks whether you will go to Highgarden, with two numbered answers"),
    ];
}
