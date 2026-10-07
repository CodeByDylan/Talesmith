using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Media;
using SkiaSharp;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Grids;
using Talesmith.Rendering;
using Talesmith.UI;

namespace Talesmith.Editor.Projects.Templates;

/// <summary>A scene with a camera and the Talesmith mark, ready for your own content.</summary>
public sealed class EmptyTemplate : IProjectTemplate
{
    /// <summary>A 1280×720 design that shows more of the world on wider or taller windows.</summary>
    internal static ViewSettings HdView { get; } = new() { Width = 1280, Height = 720, ScaleMode = ViewScaleMode.Expand };

    public string Id => "empty";

    public string Name => "Empty";

    public string Description => "A camera, a sprite and the standard folders. Start from a clean slate.";

    public IReadOnlyList<string> Tags { get; } = ["Minimal", "2D"];

    public Geometry Icon => Icons.File;

    public (Color From, Color To) Colors => (Color.Parse("#8B8EFA"), Color.Parse("#5B4FE0"));

    public int Order => 0;

    public async Task CreateAsync(ProjectScaffold scaffold, CancellationToken cancellationToken)
    {
        using var logo = TemplateArt.Logo();
        var logoGuid = await scaffold.WriteImageAsync("sprites/logo.png", logo, new TextureImportSettings { Filter = TextureFilter.Linear }, cancellationToken);

        var scene = new SceneBuilder("#20232B");
        SceneBuilder.AddCamera(scene.Entity("Main Camera", Vector2.Zero), 1);
        SceneBuilder.AddSprite(scene.Entity("Logo", Vector2.Zero), logoGuid, null, RenderLayers.Entities);
        await scaffold.WriteSceneAsync("scenes/main.tscene", scene.Document, cancellationToken);

        await scaffold.WriteSettingsAsync("scenes/main.tscene", "#20232B", HdView, "linear", cancellationToken);
        await scaffold.WriteInputProfileAsync(new JsonObject
        {
            ["Submit"] = Button("Enter", "Space"),
            ["Cancel"] = Button("Escape")
        }, cancellationToken);
    }

    internal static JsonObject Button(params string[] keys) => new()
    {
        ["kind"] = "button",
        ["bindings"] = new JsonArray([.. keys.Select(k => (JsonNode)new JsonObject { ["type"] = "key", ["key"] = k })])
    };
}

/// <summary>A hex island explored from above, with a hero, a camera and movement controls.</summary>
public sealed class HexAdventureTemplate : IProjectTemplate
{
    private const int Radius = 9;

    public string Id => "hex-adventure";

    public string Name => "Top-down hex adventure";

    public string Description => "A hex tile island with forests and mountains, a hero and WASD movement bindings.";

    public IReadOnlyList<string> Tags { get; } = ["Hex grid", "Top-down", "Tile map"];

    public Geometry Icon => Icons.Hexagon;

    public (Color From, Color To) Colors => (Color.Parse("#34D399"), Color.Parse("#0E7490"));

    public int Order => 1;

    public async Task CreateAsync(ProjectScaffold scaffold, CancellationToken cancellationToken)
    {
        using var hero = TemplateArt.Hero(new SKColor(0x3B, 0x82, 0xF6));
        var heroGuid = await scaffold.WriteImageAsync("sprites/hero.png", hero, HeroSettings(), cancellationToken);

        using var tiles = TemplateArt.HexTiles();
        var map = TileMap.Create(GridKind.HexPointyTop, TemplateArt.HexWidth, TemplateArt.HexHeight, path: "maps/island.hexy");
        var tileset = ProjectScaffold.CreateTileset("terrain", tiles, TemplateArt.HexWidth, TemplateArt.HexHeight);
        map.AddTileset(tileset);
        var ground = map.TileLayers[0];
        var spawn = GridCoord.Zero;
        for (var r = -Radius - 2; r <= Radius + 2; r++)
        {
            for (var q = -Radius - 2 - r / 2; q <= Radius + 2 - r / 2; q++)
            {
                var cell = new GridCoord(q, r);
                var tile = Terrain(map.Layout, cell);
                ground.SetCell(cell, new TileCell(tileset.Id, tile));
            }
        }

        var mapGuid = await scaffold.WriteMapAsync("maps/island.hexy", map, cancellationToken);
        var heroPosition = map.Layout.CellToWorld(spawn) + new Vector2(0, 10);

        var scene = new SceneBuilder("#1B3A5C");
        SceneBuilder.AddCamera(scene.Entity("Main Camera", heroPosition), 1.5f);
        SceneBuilder.AddTileMap(scene.Entity("Island", Vector2.Zero), mapGuid, RenderLayers.Terrain);
        var heroEntity = scene.Entity("Hero", heroPosition);
        SceneBuilder.AddSprite(heroEntity, heroGuid, "hero-0", RenderLayers.Entities, new Vector2(0.5f, 1));
        SceneBuilder.AddAnimator(heroEntity, heroGuid, "idle");
        await scaffold.WriteSceneAsync("scenes/main.tscene", scene.Document, cancellationToken);

        await scaffold.WriteSettingsAsync("scenes/main.tscene", "#1B3A5C", EmptyTemplate.HdView, "nearest", cancellationToken);
        await scaffold.WriteInputProfileAsync(new JsonObject
        {
            ["Move"] = new JsonObject
            {
                ["kind"] = "vector",
                ["bindings"] = new JsonArray(
                    new JsonObject { ["type"] = "vector", ["up"] = "W", ["down"] = "S", ["left"] = "A", ["right"] = "D" },
                    new JsonObject { ["type"] = "vector", ["up"] = "Up", ["down"] = "Down", ["left"] = "Left", ["right"] = "Right" })
            },
            ["Interact"] = EmptyTemplate.Button("E", "Space", "Enter"),
            ["Menu"] = EmptyTemplate.Button("Escape")
        }, cancellationToken);
    }

    internal static TextureImportSettings HeroSettings() => new()
    {
        Filter = TextureFilter.Nearest,
        SpriteMode = SpriteMode.Multiple,
        Grid = new SpriteGrid(TemplateArt.HeroFrame, TemplateArt.HeroFrame) { NamePrefix = "hero-", Pivot = new Vector2(0.5f, 1) },
        Animations = [new SpriteAnimationInfo("idle", ["hero-0", "hero-1", "hero-2", "hero-1", "hero-0", "hero-3"], 6)]
    };

    private static int Terrain(IGridLayout layout, GridCoord cell)
    {
        var distance = layout.Distance(cell, GridCoord.Zero) + Noise(cell.X, cell.Y) * 2.2f - 1.1f;
        if (distance > Radius + 0.5f)
            return 0;
        if (distance > Radius - 1)
            return 1;
        if (distance > Radius - 2.2f)
            return 2;
        var detail = Noise(cell.X * 3 + 11, cell.Y * 3 - 7);
        if (layout.Distance(cell, new GridCoord(3, -4)) <= 1 || (distance < 4 && detail > 0.86f && cell != GridCoord.Zero))
            return 5;
        if (detail > 0.55f && layout.Distance(cell, GridCoord.Zero) > 1)
            return 4;
        return 3;
    }

    private static float Noise(int x, int y)
    {
        var hash = (uint)(x * 374761393 + y * 668265263);
        hash = (hash ^ (hash >> 13)) * 1274126177;
        return (hash ^ (hash >> 16)) / (float)uint.MaxValue;
    }
}

/// <summary>A side-scrolling level with ground, floating platforms, coins and jump controls.</summary>
public sealed class PlatformerTemplate : IProjectTemplate
{
    private const int Width = 48;
    private const int GroundRow = 6;

    /// <summary>A 640×360 design scaled by whole numbers, which keeps pixel art crisp: 2× at 1280×720, 3× at 1920×1080; overlays are designed
    /// for 1280×720.</summary>
    private static readonly ViewSettings PixelArtView = new()
    {
        Width = 640,
        Height = 360,
        ScaleMode = ViewScaleMode.Fit,
        IntegerScale = true,
        OverlayWidth = 1280,
        OverlayHeight = 720
    };

    public string Id => "platformer";

    public string Name => "Side-scrolling platformer";

    public string Description => "A level of grass, dirt and floating platforms, spinning coins, a hero and run and jump bindings.";

    public IReadOnlyList<string> Tags { get; } = ["Square grid", "Side view", "Tile map"];

    public Geometry Icon => Icons.Flag;

    public (Color From, Color To) Colors => (Color.Parse("#FBBF24"), Color.Parse("#F97316"));

    public int Order => 2;

    public async Task CreateAsync(ProjectScaffold scaffold, CancellationToken cancellationToken)
    {
        using var hero = TemplateArt.Hero(new SKColor(0xEF, 0x44, 0x44));
        var heroGuid = await scaffold.WriteImageAsync("sprites/hero.png", hero, HexAdventureTemplate.HeroSettings(), cancellationToken);

        using var tiles = TemplateArt.PlatformerTiles();
        var tilesGuid = await scaffold.WriteImageAsync("sprites/tiles.png", tiles, new TextureImportSettings
        {
            Filter = TextureFilter.Nearest,
            SpriteMode = SpriteMode.Multiple,
            Grid = new SpriteGrid(TemplateArt.Tile, TemplateArt.Tile) { NamePrefix = "tile-" },
            Animations = [new SpriteAnimationInfo("spin", ["tile-4", "tile-5", "tile-6", "tile-7"], 8)]
        }, cancellationToken);

        var map = TileMap.Create(GridKind.Square, TemplateArt.Tile, TemplateArt.Tile, path: "maps/level.hexy");
        var tileset = ProjectScaffold.CreateTileset("tiles", tiles, TemplateArt.Tile, TemplateArt.Tile);
        map.AddTileset(tileset);
        var ground = map.TileLayers[0];
        bool IsGap(int x) => x is 17 or 18 or 31 or 32 or 33;
        for (var x = 0; x < Width; x++)
        {
            if (IsGap(x))
                continue;
            ground.SetCell(new GridCoord(x, GroundRow), new TileCell(tileset.Id, 0));
            for (var y = GroundRow + 1; y < GroundRow + 5; y++)
                ground.SetCell(new GridCoord(x, y), new TileCell(tileset.Id, y > GroundRow + 2 && (x * 7 + y) % 5 == 0 ? 2 : 1));
        }

        (int X, int Y, int Length)[] platforms = [(9, 3, 3), (15, 2, 4), (22, 3, 3), (29, 2, 5), (38, 3, 3)];
        foreach (var (px, py, length) in platforms)
        {
            for (var x = px; x < px + length; x++)
                ground.SetCell(new GridCoord(x, py), new TileCell(tileset.Id, 3));
        }

        var mapGuid = await scaffold.WriteMapAsync("maps/level.hexy", map, cancellationToken);

        var groundTop = GroundRow * TemplateArt.Tile - TemplateArt.Tile / 2f;
        var scene = new SceneBuilder("#8ED0F0");
        SceneBuilder.AddCamera(scene.Entity("Main Camera", new Vector2(8 * TemplateArt.Tile, 3 * TemplateArt.Tile)), 1);
        SceneBuilder.AddTileMap(scene.Entity("Level", Vector2.Zero), mapGuid, RenderLayers.Terrain);
        var heroEntity = scene.Entity("Hero", new Vector2(4 * TemplateArt.Tile, groundTop));
        SceneBuilder.AddSprite(heroEntity, heroGuid, "hero-0", RenderLayers.Entities, new Vector2(0.5f, 1));
        SceneBuilder.AddAnimator(heroEntity, heroGuid, "idle");
        var coins = scene.Entity("Coins", Vector2.Zero);
        foreach (var (px, py, length) in platforms)
        {
            var coin = scene.Entity($"Coin {px}", new Vector2((px + length / 2f - 0.5f) * TemplateArt.Tile, (py - 1.2f) * TemplateArt.Tile), coins);
            SceneBuilder.AddSprite(coin, tilesGuid, "tile-4", RenderLayers.Entities - 1);
            SceneBuilder.AddAnimator(coin, tilesGuid, "spin");
        }

        await scaffold.WriteSceneAsync("scenes/main.tscene", scene.Document, cancellationToken);
        await scaffold.WriteSettingsAsync("scenes/main.tscene", "#8ED0F0", PixelArtView, "nearest", cancellationToken);
        await scaffold.WriteInputProfileAsync(new JsonObject
        {
            ["Run"] = new JsonObject
            {
                ["kind"] = "axis",
                ["bindings"] = new JsonArray(
                    new JsonObject { ["type"] = "axis", ["negative"] = "A", ["positive"] = "D" },
                    new JsonObject { ["type"] = "axis", ["negative"] = "Left", ["positive"] = "Right" })
            },
            ["Jump"] = EmptyTemplate.Button("Space", "W", "Up"),
            ["Menu"] = EmptyTemplate.Button("Escape")
        }, cancellationToken);
    }
}
