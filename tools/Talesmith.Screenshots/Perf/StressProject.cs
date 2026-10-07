using System.Numerics;
using System.Text.Json.Nodes;
using SkiaSharp;
using Talesmith.Assets.Maps;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Grids;
using Talesmith.Rendering;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Screenshots.Perf;

/// <summary>A project for measuring the editor at scale: a hex map of millions of cells and a scene of thousands of sprites, lights and
/// particle emitters.</summary>
internal static class StressProject
{
    private const int GroupSize = 100;

    public static async Task<string> CreateAsync(string parent, int columns, int rows, int entities)
    {
        var scaffold = new ProjectScaffold(Path.Combine(parent, "Stress"), "Stress");
        scaffold.CreateStructure();
        using var hero = TemplateArt.Hero(new SKColor(0x3B, 0x82, 0xF6));
        var heroGuid = await scaffold.WriteImageAsync("sprites/hero.png", hero, HexAdventureTemplate.HeroSettings());

        using var tiles = TemplateArt.HexTiles();
        var map = TileMap.Create(GridKind.HexPointyTop, TemplateArt.HexWidth, TemplateArt.HexHeight, path: "maps/world.hexy");
        var tileset = ProjectScaffold.CreateTileset("terrain", tiles, TemplateArt.HexWidth, TemplateArt.HexHeight);
        map.AddTileset(tileset);
        var layer = map.TileLayers[0];
        var palette = Math.Max(1, Math.Min(tileset.TileCount, 8));
        var noise = new ValueNoise(7);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var elevation = noise.Fractal(column * 0.015, row * 0.015, 5);
                var r = row - rows / 2;
                var q = column - columns / 2 - (r - (r & 1)) / 2;
                layer.SetCell(new GridCoord(q, r), new TileCell(tileset.Id, (int)Math.Clamp(elevation * palette, 0, palette - 1)));
            }
        }

        var mapGuid = await scaffold.WriteMapAsync("maps/world.hexy", map);

        var scene = new SceneBuilder("#1B3A5C");
        SceneBuilder.AddCamera(scene.Entity("Main Camera", Vector2.Zero), 1);
        SceneBuilder.AddTileMap(scene.Entity("World", Vector2.Zero), mapGuid, 100);
        var side = (int)Math.Ceiling(Math.Sqrt(entities));
        EntityDocument? group = null;
        for (var i = 0; i < entities; i++)
        {
            if (i % GroupSize == 0)
                group = scene.Entity($"Group {i / GroupSize + 1}", Vector2.Zero);
            var position = new Vector2((i % side - side / 2) * 48, (i / side - side / 2) * 48);
            var entity = scene.Entity($"Entity {i + 1}", position, group);
            switch (i % 15)
            {
                case 0:
                    entity.Components.Add(new ComponentDocument("Light2D", new JsonObject { ["radius"] = 160, ["color"] = "#FFD9A0" }));
                    break;
                case 7:
                    entity.Components.Add(new ComponentDocument("ParticleEmitter", new JsonObject()));
                    break;
                default:
                    SceneBuilder.AddSprite(entity, heroGuid, $"hero-{i % 4}", 300, new Vector2(0.5f, 1));
                    break;
            }
        }

        await scaffold.WriteSceneAsync("scenes/main.tscene", scene.Document);
        await scaffold.WriteSettingsAsync("scenes/main.tscene", "#1B3A5C", ViewSettings.Unscaled);
        return scaffold.Folder;
    }

    private sealed class ValueNoise(int seed)
    {
        public double Fractal(double x, double y, int octaves)
        {
            double sum = 0, amplitude = 0.5, frequency = 1, total = 0;
            for (var i = 0; i < octaves; i++)
            {
                sum += Sample(x * frequency, y * frequency) * amplitude;
                total += amplitude;
                amplitude *= 0.5;
                frequency *= 2.03;
            }

            return sum / total;
        }

        private double Sample(double x, double y)
        {
            var x0 = (int)Math.Floor(x);
            var y0 = (int)Math.Floor(y);
            var tx = Smooth(x - x0);
            var ty = Smooth(y - y0);
            var a = Lerp(Hash(x0, y0), Hash(x0 + 1, y0), tx);
            var b = Lerp(Hash(x0, y0 + 1), Hash(x0 + 1, y0 + 1), tx);
            return Lerp(a, b, ty);
        }

        private double Hash(int x, int y)
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0x1000000;
        }

        private static double Smooth(double t) => t * t * (3 - 2 * t);

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    }
}
