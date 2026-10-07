using Talesmith.Screenshots.Scenes;

namespace Talesmith.Screenshots.Docs;

/// <summary>The screenshots of the scripting section, in website/static/img/screenshots/scripting.</summary>
internal static class ScriptingShots
{
    public static IReadOnlyList<DocsShot> All { get; } =
    [
        new("script-fields", new ScriptFieldsScene(), "The Inspector with Lantern Grove's Player Controller script, its fields grouped under Running, Jumping, Dash and Falling headers with sliders for ranges"),
        new("script-errors", new EditorScriptErrorScene(), "A script compile error in the console, filtered to scripts, with the status bar item"),
        new("create-script", new CreateScriptScene(), "The Create script dialog with the class name Coin, the Script template selected and the file path it will write"),
        new("analyzer-warnings", new AnalyzerWarningsScene(), "Game-loop analyzer warnings in the console for a script that allocates, blocks and uses async void, with the status bar counting the warnings"),
        new("hot-reload", new HotReloadScene(), "Play mode with the Scripts reloaded toast after a script was saved"),
        new("hot-reload-restart", new HotReloadRestartScene(), "Lantern Grove in play mode with the Restart play mode toast and status bar item after a script was saved"),
        new("lantern-grove-play", new LanternGrovePlayScene(), "Lantern Grove running in the Game panel, the player in a dark grove lit by lanterns and a campfire, with the Player Controller's live values in the Inspector"),
    ];
}
