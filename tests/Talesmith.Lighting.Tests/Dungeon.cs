using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Ecs;
using Talesmith.Grids;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;

namespace Talesmith.Lighting.Tests;

/// <summary>A small dungeon of colored tiles: a floor, walls on a collision layer, torches, a crate, a glowing crystal and a HUD bar.</summary>
public static class Dungeon
{
    public const int Cell = 32;
    public const int Columns = 20;
    public const int Rows = 12;

    public static readonly Vector2 Torch = new(5 * Cell, 3 * Cell);
    public static readonly Vector2 Lantern = new(15 * Cell, 8 * Cell);
    public static readonly Vector2 Crate = new(8 * Cell, 5 * Cell);
    public static readonly Vector2 Crystal = new(16 * Cell, 2 * Cell);
    public static readonly Rect2 Hud = new(8, 8, 200, 18);

    private const int Floor = 0;
    private const int FloorDark = 1;
    private const int Wall = 2;

    public static void Build(World world, IRenderer renderer)
    {
        var camera = world.Create();
        world.Set(camera, new Camera(new Vector2((Columns - 1) * Cell / 2f, (Rows - 1) * Cell / 2f)));

        var map = world.Create();
        world.Set(map, new Transform(Vector2.Zero));
        world.Set(map, new TileMapComponent(CreateMap()));

        AddTorch(world, Torch, new Color(255, 170, 90), 1.5f);
        AddTorch(world, Lantern, new Color(150, 200, 255), 1.3f);

        var crate = world.Create();
        world.Set(crate, new Transform(Crate, 0.4f));
        world.Set(crate, new Sprite(renderer.WhiteTexture) { Size = new Vector2(26), Tint = new Color(140, 95, 60) });
        world.Set(crate, new ShadowCaster2D { Size = new Vector2(26) });

        var crystal = world.Create();
        world.Set(crystal, new Transform(Crystal, MathF.PI / 4));
        world.Set(crystal, new Sprite(renderer.WhiteTexture) { Size = new Vector2(14), Tint = new Color(120, 255, 200) });
        world.Set(crystal, new Emissive { Color = new Color(120, 255, 200) });

        var hud = world.Create();
        world.Set(hud, new Transform(Hud.Position));
        world.Set(hud, new Sprite(renderer.WhiteTexture, RenderLayers.Overlay) { Size = Hud.Size, Origin = Vector2.Zero });
    }

    public static Entity AddTorch(World world, Vector2 position, Color color, float intensity)
    {
        var torch = world.Create();
        world.Set(torch, new Transform(position));
        world.Set(torch, new Light2D
        {
            Color = color,
            Intensity = intensity,
            Radius = 260,
            CastsShadows = true,
            ShadowSoftness = 0.5f,
            Animation = LightAnimation.Flicker,
            AnimationAmount = 0.15f
        });
        return torch;
    }

    public static void Darken(LightingEnvironment environment) => environment.AmbientColor = new Color(60, 64, 110);

    private static TileMap CreateMap()
    {
        var tiles = new Dictionary<int, TileInfo>
        {
            [Floor] = new(Floor, "floor", new Color(176, 166, 146), [], PropertySet.Empty),
            [FloorDark] = new(FloorDark, "floor-dark", new Color(160, 150, 132), [], PropertySet.Empty),
            [Wall] = new(Wall, "wall", new Color(92, 88, 104), [], PropertySet.Empty)
        };
        var tileset = new Tileset(1, "colors", Cell, Cell, null, 0, 0, 3, 3, tiles) { Placement = TilePlacement.Center };
        var ground = new TileLayer("Ground", 4);
        var walls = new TileLayer("Walls", 4) { Role = LayerRole.Collision };
        for (var y = 0; y < Rows; y++)
        {
            for (var x = 0; x < Columns; x++)
            {
                ground.SetCell(new GridCoord(x, y), new TileCell(1, (x + y) % 2 == 0 ? Floor : FloorDark));
                if (IsWall(x, y))
                    walls.SetCell(new GridCoord(x, y), new TileCell(1, Wall));
            }
        }

        return new TileMap("dungeon", new SquareLayout(Cell, Cell), 4, [tileset], [ground, walls], PropertySet.Empty);
    }

    private static bool IsWall(int x, int y) =>
        x == 0 || y == 0 || x == Columns - 1 || y == Rows - 1
        || (x == 10 && y is >= 2 and <= 6)
        || (y == 6 && x is >= 10 and <= 13)
        || (x is 4 or 5 && y == 7)
        || (x == 15 && y == 3);
}
