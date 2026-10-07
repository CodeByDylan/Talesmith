using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Grids;

namespace Talesmith.Assets.Tests.Maps;

/// <summary>Auto-tiling cases following the rules of Hexy's auto-tiler, on square grids and both hex orientations.</summary>
public sealed class AutoTilerTests
{
    public static TheoryData<GridKind> HexKinds => [GridKind.HexPointyTop, GridKind.HexFlatTop];

    /// <summary>A square terrain with one rule per neighbor combination: the tile id is the mask of E, N, W and S neighbors.</summary>
    private static Terrain SquareGround(string name = "Ground", int tileset = 1)
    {
        var rules = new List<AutoTileRule>();
        for (var mask = 0; mask < 16; mask++)
        {
            var pattern = new string([.. Enumerable.Range(0, 4).Select(d => (mask & (1 << d)) != 0 ? '+' : '-')]);
            rules.Add(new AutoTileRule(pattern, [new WeightedTile(mask)]));
        }

        return new Terrain(name, tileset, 15, rules);
    }

    /// <summary>A hex terrain: lone cells use tile 0, cells with one neighbor use tile 1 turned toward it, others the base tile 2.</summary>
    private static Terrain HexIsland() => new("Island", 1, 2,
    [
        new AutoTileRule("------", [new WeightedTile(0)]),
        new AutoTileRule("+-----", [new WeightedTile(1)], matchRotations: true)
    ]);

    [Fact]
    public void ALoneSquareCellPicksTheTileWithoutNeighbors()
    {
        var (map, edit) = Setup(GridKind.Square, SquareGround());

        AutoTiler.Paint(edit, map.Ground(), [GridCoord.Zero], map.Terrains[0]);

        Assert.Equal(TestMaps.Tile(0), map.Ground().GetCell(GridCoord.Zero));
    }

    [Fact]
    public void PaintingNextToTerrainUpdatesTheTransitionsOfBothCells()
    {
        var (map, edit) = Setup(GridKind.Square, SquareGround());
        var layer = map.Ground();

        AutoTiler.Paint(edit, layer, [GridCoord.Zero], map.Terrains[0]);
        AutoTiler.Paint(edit, layer, [new GridCoord(1, 0)], map.Terrains[0]);

        Assert.Equal(TestMaps.Tile(1), layer.GetCell(GridCoord.Zero));
        Assert.Equal(TestMaps.Tile(4), layer.GetCell(new GridCoord(1, 0)));
    }

    [Fact]
    public void ABlockGetsCornersEdgesAndAFullCenter()
    {
        var (map, edit) = Setup(GridKind.Square, SquareGround());
        var block = new CellSet();
        GridShapes.Rectangle(GridTopology.Square, new GridCoord(-1, -1), new GridCoord(1, 1), filled: true, block);

        AutoTiler.Paint(edit, map.Ground(), block, map.Terrains[0]);

        var layer = map.Ground();
        Assert.Equal(TestMaps.Tile(15), layer.GetCell(GridCoord.Zero));
        Assert.Equal(TestMaps.Tile(1 | 8), layer.GetCell(new GridCoord(-1, -1)));
        Assert.Equal(TestMaps.Tile(1 | 2 | 4), layer.GetCell(new GridCoord(0, 1)));
        Assert.Equal(TestMaps.Tile(2 | 4), layer.GetCell(new GridCoord(1, 1)));
    }

    [Fact]
    public void ErasingTerrainClearsTheCellsAndReresolvesTheirNeighbors()
    {
        var (map, edit) = Setup(GridKind.Square, SquareGround());
        var layer = map.Ground();
        var block = new CellSet();
        GridShapes.Rectangle(GridTopology.Square, new GridCoord(-1, -1), new GridCoord(1, 1), filled: true, block);
        AutoTiler.Paint(edit, layer, block, map.Terrains[0]);

        AutoTiler.Paint(edit, layer, [GridCoord.Zero], null);

        Assert.True(layer.GetCell(GridCoord.Zero).IsEmpty);
        Assert.Equal(TestMaps.Tile(2 | 8), layer.GetCell(new GridCoord(1, 0)));
        Assert.Equal(TestMaps.Tile(1 | 4), layer.GetCell(new GridCoord(0, -1)));
    }

    [Fact]
    public void OtherTerrainsCountAsDifferentAndKeepTheirOwnRules()
    {
        var map = TestMaps.Create(GridKind.Square);
        var other = Tileset.FromColors("Other", [.. Enumerable.Range(0, 16).Select(i => ($"{i}", Mathematics.Color.White))]);
        map.AddTileset(other);
        map.AddTerrain(SquareGround());
        map.AddTerrain(SquareGround("Water", other.Id));
        var edit = new TileEdit(map);
        var layer = map.Ground();

        AutoTiler.Paint(edit, layer, [GridCoord.Zero, new GridCoord(1, 0)], map.Terrains[0]);
        AutoTiler.Paint(edit, layer, [new GridCoord(2, 0)], map.Terrains[1]);

        Assert.Equal(TestMaps.Tile(4), layer.GetCell(new GridCoord(1, 0)));
        Assert.Equal(new TileCell(other.Id, 0), layer.GetCell(new GridCoord(2, 0)));
    }

    [Theory]
    [MemberData(nameof(HexKinds))]
    public void HexRulesTurnTheirTileTowardTheMatchingNeighbor(GridKind kind)
    {
        var (map, edit) = Setup(kind, HexIsland());
        var layer = map.Ground();
        var topology = map.Layout.Topology;

        AutoTiler.Paint(edit, layer, [GridCoord.Zero], map.Terrains[0]);
        Assert.Equal(TestMaps.Tile(0), layer.GetCell(GridCoord.Zero));

        for (var direction = 0; direction < 6; direction++)
        {
            var neighbor = topology.Direction(direction);
            AutoTiler.Paint(edit, layer, [neighbor], map.Terrains[0]);

            var expected = (6 - direction) % 6;
            Assert.Equal(TestMaps.Tile(1, rotation: expected), layer.GetCell(GridCoord.Zero));
            Assert.Equal(TestMaps.Tile(1, rotation: (expected + 3) % 6), layer.GetCell(neighbor));
            AutoTiler.Paint(edit, layer, [neighbor], null);
            Assert.Equal(TestMaps.Tile(0), layer.GetCell(GridCoord.Zero));
        }
    }

    [Theory]
    [MemberData(nameof(HexKinds))]
    public void HexCellsWithSeveralNeighborsFallBackToTheBaseTile(GridKind kind)
    {
        var (map, edit) = Setup(kind, HexIsland());
        var cells = new CellSet();
        map.Layout.Topology.Range(GridCoord.Zero, 1, cells);

        AutoTiler.Paint(edit, map.Ground(), cells, map.Terrains[0]);

        Assert.Equal(TestMaps.Tile(2), map.Ground().GetCell(GridCoord.Zero));
        Assert.All(cells, cell => Assert.True(map.Terrains[0].Contains(map.Ground().GetCell(cell))));
    }

    [Fact]
    public void RulesWrittenForAnotherGridNeverMatch()
    {
        var (map, edit) = Setup(GridKind.HexPointyTop, SquareGround());

        AutoTiler.Paint(edit, map.Ground(), [GridCoord.Zero], map.Terrains[0]);

        Assert.Equal(TestMaps.Tile(15), map.Ground().GetCell(GridCoord.Zero));
    }

    [Fact]
    public void WeightedRuleTilesArePickedDeterministicallyPerCell()
    {
        var terrain = new Terrain("Grass", 1, 9,
        [
            new AutoTileRule("****", [new WeightedTile(10, 1), new WeightedTile(11, 1), new WeightedTile(12, 0)])
        ]);
        var (map, edit) = Setup(GridKind.Square, terrain);
        var cells = new CellSet();
        GridShapes.Rectangle(GridTopology.Square, new GridCoord(0, 0), new GridCoord(15, 15), filled: true, cells);

        AutoTiler.Paint(edit, map.Ground(), cells, map.Terrains[0]);
        var first = TestMaps.Cells(map.Ground());
        var tiles = first.Select(c => c.Tile.TileId).Distinct().Order().ToArray();
        var again = AutoTiler.ComputeChanges(map.Ground(), map.Terrains, GridTopology.Square, cells, map.Terrains[0]);

        Assert.Equal([10, 11], tiles);
        Assert.Empty(again);
    }

    [Fact]
    public void ResolveWorksFromAnyTileLookup()
    {
        var terrain = HexIsland();
        var tiles = new Dictionary<GridCoord, TileCell> { [GridCoord.Zero] = TestMaps.Tile(2), [new GridCoord(0, 1)] = TestMaps.Tile(2) };

        var tile = AutoTiler.Resolve(terrain, GridTopology.HexPointyTop, GridCoord.Zero, c => tiles.GetValueOrDefault(c));

        Assert.Equal(TestMaps.Tile(1, rotation: 1), tile);
    }

    [Fact]
    public void TerrainPaintingUndoesInOneStep()
    {
        var (map, edit) = Setup(GridKind.Square, SquareGround());
        map.Ground().SetCell(new GridCoord(5, 5), TestMaps.Tile(3));
        var before = TestMaps.Cells(map.Ground());

        AutoTiler.Paint(edit, map.Ground(), [GridCoord.Zero, new GridCoord(1, 0), new GridCoord(1, 1)], map.Terrains[0]);
        edit.Commit()!.Invert().Apply(map);

        Assert.Equal(before, TestMaps.Cells(map.Ground()));
    }

    private static (TileMap Map, TileEdit Edit) Setup(GridKind kind, Terrain terrain)
    {
        var map = TestMaps.Create(kind);
        map.AddTerrain(terrain);
        return (map, new TileEdit(map));
    }
}
