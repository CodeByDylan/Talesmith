using System.Numerics;
using System.Text.Json.Nodes;
using SkiaSharp;
using Talesmith.Assets;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Grids;
using Talesmith.Lighting;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Serialization;
using Talesmith.VFX;
using Talesmith.VFX.Presets;

namespace Talesmith.Benchmarks;

/// <summary>Writes game folders that put the engine under load, for the player's headless benchmark.</summary>
/// <remarks>
/// <c>scale</c> shows 10,000 sprites, 50,000 particles from ten emitters and 32 shadow-casting lights at once, over a 2.1 million cell
/// map with a collision layer that casts shadows. <c>scale-flyover</c> flies the camera across the same map instead, which measures
/// chunk streaming and meshing.
/// </remarks>
public static class ScaleScene
{
    public const int Sprites = 10_000;
    public const int Emitters = 10;
    public const int ParticlesPerEmitter = 5_000;
    public const int Lights = 32;
    public const int MapWidth = 1500;
    public const int MapHeight = 1400;
    public const int Cell = 32;

    private const int ViewWidth = 1280;
    private const int ViewHeight = 720;

    public static async Task WriteAsync(string folder)
    {
        var map = BuildMap();
        await WriteGameAsync(Path.Combine(folder, "scale"), map, flyover: false);
        await WriteGameAsync(Path.Combine(folder, "scale-flyover"), map, flyover: true);
    }

    private static async Task WriteGameAsync(string folder, TileMap map, bool flyover)
    {
        var assets = Path.Combine(folder, "assets");
        Directory.CreateDirectory(Path.Combine(assets, "config"));
        Directory.CreateDirectory(Path.Combine(assets, "scenes"));
        Directory.CreateDirectory(Path.Combine(assets, "maps"));
        Directory.CreateDirectory(Path.Combine(assets, "sprites"));
        Directory.CreateDirectory(Path.Combine(assets, "particles"));

        await HexyMapWriter.SaveAsync(map, Path.Combine(assets, "maps", "world.hexy"));
        var mapGuid = await MetaAsync(assets, "maps/world.hexy", null);
        await File.WriteAllBytesAsync(Path.Combine(assets, "sprites", "shapes.png"), ShapesImage());
        var shapes = await MetaAsync(assets, "sprites/shapes.png", new TextureImportSettings
        {
            Filter = TextureFilter.Linear,
            SpriteMode = SpriteMode.Multiple,
            Grid = new SpriteGrid(32, 32) { NamePrefix = "shape-" }
        });

        var center = new Vector2(MapWidth / 2 * Cell, MapHeight / 2 * Cell);
        var scene = SceneDocument.Create();
        scene.Environment.ClearColor = Color.Parse("#101420");
        scene.Environment.AmbientLight = Color.Parse("#4A5680");
        scene.Environment.AmbientIntensity = 0.35f;
        new SceneLightingSettings
        {
            Quality = LightingQuality.Custom,
            CustomQuality = LightingQualitySettings.Medium with { MaxLights = 64, MaxShadowedLights = Lights }
        }.Write(scene.Environment);

        var level = Entity(scene, "World", Vector2.Zero);
        Add(level, "TileMapRenderer", new JsonObject { ["map"] = mapGuid.ToString(), ["renderLayer"] = RenderLayers.Terrain });

        var camera = Entity(scene, "Camera", center);
        var cameraData = new JsonObject { ["zoom"] = 1, ["followSharpness"] = 0 };
        Add(camera, "Camera", cameraData);
        if (flyover)
        {
            var probe = Entity(scene, "Probe", center - new Vector2(3000, 1500));
            Add(probe, "Collider2D", new JsonObject { ["shape"] = "circle", ["radius"] = 4, ["isTrigger"] = true, ["collisionMask"] = 0 });
            Add(probe, "Rigidbody2D", new JsonObject { ["type"] = "kinematic", ["velocity"] = new JsonArray(600, 300), ["gravityScale"] = 0 });
            cameraData["target"] = probe.Id.ToString("N");
        }

        var random = new Random(42);
        Vector2 InView(float margin = 0) => center + new Vector2(
            (random.NextSingle() - 0.5f) * (ViewWidth - margin * 2),
            (random.NextSingle() - 0.5f) * (ViewHeight - margin * 2));

        for (var i = 0; i < Sprites; i++)
        {
            var sprite = Entity(scene, $"Sprite {i}", InView(8));
            var tint = Hue(random.NextSingle() * 360, 0.5f);
            Add(sprite, "Sprite", new JsonObject
            {
                ["texture"] = shapes.ToString(),
                ["sprite"] = $"shape-{i % 8}",
                ["tint"] = tint.ToString(),
                ["layer"] = RenderLayers.Entities + i % 4,
                ["sortByY"] = i % 2 == 0
            });
        }

        for (var i = 0; i < Emitters; i++)
        {
            var path = $"particles/emitter-{i}.tparticles";
            ParticlePresetSerializer.Save(Path.Combine(assets, path), new ParticlePreset($"Emitter {i}", Particles(i)));
            var preset = await MetaAsync(assets, path, null);
            var emitter = Entity(scene, $"Emitter {i}", InView(120));
            Add(emitter, "ParticleEmitter", new JsonObject { ["preset"] = preset.ToString() });
        }

        for (var i = 0; i < Lights; i++)
        {
            var light = Entity(scene, $"Light {i}", InView(40));
            Add(light, "Light2D", new JsonObject
            {
                ["color"] = Hue(i * 360f / Lights, 0.45f).ToString(),
                ["intensity"] = 0.9,
                ["radius"] = 240,
                ["castsShadows"] = true,
                ["animation"] = "flicker",
                ["animationSpeed"] = 4,
                ["animationAmount"] = 0.15
            });
        }

        await File.WriteAllTextAsync(Path.Combine(assets, "scenes", "scale.tscene"), DocumentSerializer.Write(scene));
        await MetaAsync(assets, "scenes/scale.tscene", null);
        var title = flyover ? "Scale flyover" : "Scale";
        await File.WriteAllTextAsync(Path.Combine(assets, "config", "game.json"), $$"""
            {
              "title": "{{title}}",
              "renderer": "auto",
              "vSync": false,
              "startScene": "scenes/scale.tscene"
            }
            """);
    }

    private static TileMap BuildMap()
    {
        var map = TileMap.Create(GridKind.Square, Cell, Cell, path: "maps/world.hexy");
        var colors = Tileset.FromColors("Ground", [("Moss", Color.Parse("#3E5B3A")), ("Grass", Color.Parse("#4F7347")), ("Dirt", Color.Parse("#6B5136")),
            ("Stone", Color.Parse("#6A6E78")), ("Water", Color.Parse("#2D4E73")), ("Wall", Color.Parse("#3B3640"))], Cell, Cell);
        map.AddTileset(colors);
        var ground = map.TileLayers[0];
        var walls = map.CreateTileLayer("Walls", LayerRole.Collision);
        map.AddLayer(walls);
        for (var y = 0; y < MapHeight; y++)
        {
            for (var x = 0; x < MapWidth; x++)
            {
                var noise = MathF.Sin(x * 0.045f) + MathF.Sin(y * 0.038f) + MathF.Sin((x + y) * 0.021f);
                var tile = noise switch
                {
                    < -1.6f => 4,
                    < -0.4f => 0,
                    < 0.9f => 1,
                    < 1.8f => 2,
                    _ => 3
                };
                ground.SetCell(new GridCoord(x, y), new TileCell(colors.Id, tile));
                if (x % 9 == 4 && y % 7 == 3)
                    walls.SetCell(new GridCoord(x, y), new TileCell(colors.Id, 5));
            }
        }

        return map;
    }

    private static ParticleSettings Particles(int index)
    {
        const float lifetime = 2;
        var settings = new ParticleSettings { Seed = index + 1, MaxParticles = ParticlesPerEmitter, Duration = lifetime, Prewarm = true };
        settings.Emission.RateOverTime = ParticlesPerEmitter / lifetime;
        settings.Shape.Kind = ParticleShapeKind.Circle;
        settings.Shape.Radius = 90;
        settings.Shape.Direction = ParticleDirectionMode.Random;
        settings.Initial.Lifetime = new MinMaxFloat(lifetime * 0.9f, lifetime);
        settings.Initial.Speed = new MinMaxFloat(10, 60);
        settings.Initial.Size = new MinMaxFloat(4, 10);
        settings.Initial.Color = new MinMaxColor(Color.Parse("#FFE9A0"), Color.Parse("#A0D8FF"));
        settings.Noise.Enabled = index % 2 == 0;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient([new(0, Color.Parse("#00FFFFFF")), new(0.2f, Color.Parse("#FFFFFFFF")), new(1, Color.Parse("#00FFFFFF"))]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Glow;
        settings.Renderer.Blend = BlendMode.Additive;
        return settings;
    }

    private static byte[] ShapesImage()
    {
        using var bitmap = new SKBitmap(256, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White };
        for (var i = 0; i < 8; i++)
        {
            var x = i * 32 + 16f;
            switch (i % 4)
            {
                case 0:
                    canvas.DrawCircle(x, 16, 13, paint);
                    break;
                case 1:
                    canvas.DrawRoundRect(new SKRect(x - 12, 4, x + 12, 28), 5, 5, paint);
                    break;
                case 2:
                    using (var path = new SKPath())
                    {
                        path.MoveTo(x, 3);
                        path.LineTo(x + 13, 28);
                        path.LineTo(x - 13, 28);
                        path.Close();
                        canvas.DrawPath(path, paint);
                    }

                    break;
                default:
                    canvas.DrawOval(new SKRect(x - 14, 9, x + 14, 23), paint);
                    break;
            }
        }

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static EntityDocument Entity(SceneDocument scene, string name, Vector2 position)
    {
        var entity = new EntityDocument { Id = Guid.NewGuid(), Name = name };
        entity.Components.Add(new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(position.X, position.Y) }));
        scene.Entities.Add(entity);
        return entity;
    }

    private static Color Hue(float hue, float saturation)
    {
        var c = saturation;
        var h = hue / 60 % 6;
        var x = c * (1 - MathF.Abs(h % 2 - 1));
        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0f),
            < 2 => (x, c, 0f),
            < 3 => (0f, c, x),
            < 4 => (0f, x, c),
            < 5 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        var m = 1 - c;
        return Color.FromVector4(new Vector4(r + m, g + m, b + m, 1));
    }

    private static void Add(EntityDocument entity, string type, JsonObject data) => entity.Components.Add(new ComponentDocument(type, data));

    private static async Task<AssetGuid> MetaAsync(string assets, string path, object? settings)
    {
        var guid = AssetGuid.NewGuid();
        var importer = path.EndsWith(".png", StringComparison.Ordinal) ? TextureImporter.ImporterId : null;
        var meta = new AssetMeta(guid) { Importer = importer, ImporterVersion = importer is null ? 0 : 1 };
        if (settings is not null)
            meta = meta.WithSettings(settings);
        await AssetMetaFile.WriteAsync(AssetMetaFile.GetMetaPath(Path.Combine(assets, path)), meta);
        return guid;
    }
}
