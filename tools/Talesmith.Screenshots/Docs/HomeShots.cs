using Talesmith.Screenshots.Scenes;

namespace Talesmith.Screenshots.Docs;

/// <summary>The screenshots of the homepage and the social card, in website/static/img/screenshots/home.</summary>
internal static class HomeShots
{
    /// <summary>The screenshot the social card is built from.</summary>
    public const string Hero = "editor";

    public static IReadOnlyList<DocsShot> All { get; } =
    [
        new(Hero, new TileMapScene(), "The Talesmith editor painting a hex island in the Tile Mapping layout, with the map's layers, tileset palette and inspector"),
        new("tile-maps", new TileMapSquareScene(), "A rectangular platformer level in the Tile Mapping layout with the brush over the ground"),
        new("scripts", new EditorScriptErrorScene(), "A script compile error in the console with its file and line, and the status bar item"),
        new("particles", new ParticleEditorScene(), "The particle editor on a campfire effect with a live preview"),
        new("lighting", new LightingPanelScene(), "The Lighting panel on a night scene with torches, a lamp and shadow casters"),
        new("play-mode", new EditorPlayScene(), "Play mode with the game running inside the editor"),
        new("sprite-editor", new SpriteEditorScene(), "The sprite editor slicing a character sheet into sprites and animations"),
        new("plugins", new EditorPluginsScene(), "The Plugins panel listing a game's plugins"),
    ];
}
