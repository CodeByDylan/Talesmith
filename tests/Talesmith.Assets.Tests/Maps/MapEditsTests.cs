using System.Numerics;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Tests.Maps;

public sealed class MapEditsTests
{
    [Fact]
    public void LayerInsertMoveChangeAndRemoveUndoInOrder()
    {
        var map = TestMaps.Create();
        var objects = new ObjectLayer("Objects");
        var history = new History(map);

        history.Do(MapEdits.InsertLayer(objects, 0));
        history.Do(MapEdits.MoveLayer(objects, 1));
        history.Do(MapEdits.ChangeLayer(objects, LayerSettings.Of(objects) with { Name = "Spawns", Role = LayerRole.Trigger, Opacity = 0.5f, Color = new Color(1, 2, 3) }));
        history.Do(MapEdits.RemoveLayer(map.Ground()));

        Assert.Equal(["Spawns"], map.Layers.Select(l => l.Name));
        history.UndoAll();
        Assert.Equal(["Ground"], map.Layers.Select(l => l.Name));
        Assert.Equal(("Objects", LayerRole.Object, 1f, (Color?)null), (objects.Name, objects.Role, objects.Opacity, objects.Color));
        history.RedoAll();
        Assert.Equal(["Spawns"], map.Layers.Select(l => l.Name));
        Assert.Equal((LayerRole.Trigger, 0.5f, (Color?)new Color(1, 2, 3)), (objects.Role, objects.Opacity, objects.Color));
    }

    [Fact]
    public void RemovingATilesetRemovesWhatUsesItAndUndoRestoresAll()
    {
        var map = TestMaps.Create();
        var other = Tileset.FromColors("Other", [("Black", Color.Black)]);
        map.AddTileset(other);
        var ground = map.Ground();
        ground.SetCell(new GridCoord(1, 1), TestMaps.Tile(3));
        ground.SetCell(new GridCoord(40, 2), TestMaps.Tile(4));
        ground.SetCell(new GridCoord(2, 2), new TileCell(other.Id, 0));
        var objects = new ObjectLayer("Objects", [Image(1, TestMaps.Tile(5)), Image(2, new TileCell(other.Id, 0)), Image(3, TestMaps.Tile(6))]);
        map.AddLayer(objects);
        map.AddTerrain(new Terrain("Grass", 1, 4, []));
        map.AddTerrain(new Terrain("Night", other.Id, 0, []));
        var tileset = map.Tilesets[0];
        var cellsBefore = TestMaps.Cells(ground);

        var undo = MapEdits.RemoveTileset(map, tileset).Apply(map);

        Assert.Equal([other], map.Tilesets);
        Assert.Equal([new PlacedTile(new GridCoord(2, 2), new TileCell(other.Id, 0))], TestMaps.Cells(ground));
        Assert.Equal([2], objects.Objects.Select(o => o.Id));
        Assert.Equal(["Night"], map.Terrains.Select(t => t.Name));

        var redo = undo.Apply(map);
        Assert.Equal([tileset, other], map.Tilesets);
        Assert.Same(tileset, map.FindTileset(1));
        Assert.Equal(cellsBefore, TestMaps.Cells(ground));
        Assert.Equal([1, 2, 3], objects.Objects.Select(o => o.Id));
        Assert.Equal(["Grass", "Night"], map.Terrains.Select(t => t.Name));

        redo.Apply(map);
        Assert.Equal([other], map.Tilesets);
    }

    [Fact]
    public void TilesetAndTileChangesUndo()
    {
        var map = TestMaps.Create();
        var tileset = map.Tilesets[0];
        var history = new History(map);
        Vector2[] square = [new(-8, -8), new(8, -8), new(8, 8), new(-8, 8)];

        history.Do(MapEdits.ChangeTileset(tileset, TilesetSettings.Of(tileset) with { Name = "Palette", TileWidth = 32, Columns = 4 }));
        history.Do(MapEdits.ChangeTile(tileset, 2, tileset.Find(2)! with { Collision = [square] }));
        history.Do(MapEdits.ChangeTile(tileset, 3, null));
        history.Do(MapEdits.InsertTileset(Tileset.FromColors("Extra", [("White", Color.White)]), 0));

        Assert.Equal(("Palette", 32, 4), (tileset.Name, tileset.TileWidth, tileset.Columns));
        Assert.Single(tileset.Find(2)!.Collision);
        Assert.Null(tileset.Find(3));
        Assert.Equal(2, map.Tilesets.Count);

        history.UndoAll();
        Assert.Equal(("Colors", 64, 8), (tileset.Name, tileset.TileWidth, tileset.Columns));
        Assert.Empty(tileset.Find(2)!.Collision);
        Assert.Equal("Color 3", tileset.Find(3)!.Name);
        Assert.Single(map.Tilesets);
    }

    [Fact]
    public void TerrainEditsUndo()
    {
        var map = TestMaps.Create();
        var grass = new Terrain("Grass", 1, 4, []);
        var history = new History(map);

        history.Do(MapEdits.InsertTerrain(grass, 0));
        history.Do(MapEdits.ReplaceTerrain(grass.WithRules([new AutoTileRule("------", [new WeightedTile(6)])])));
        history.Do(MapEdits.InsertTerrain(new Terrain("Sand", 1, 8, []), 0));
        history.Do(MapEdits.RemoveTerrain(map.Terrains[1]));

        Assert.Equal(["Sand"], map.Terrains.Select(t => t.Name));
        history.Undo();
        Assert.Single(map.Terrains[1].Rules);
        history.UndoAll();
        Assert.Empty(map.Terrains);
        history.RedoAll();
        Assert.Equal(["Sand"], map.Terrains.Select(t => t.Name));
    }

    [Fact]
    public void ObjectEditsUndo()
    {
        var map = TestMaps.Create();
        var spawns = new ObjectLayer("Spawns");
        var props = new ObjectLayer("Props");
        map.AddLayer(spawns);
        map.AddLayer(props);
        var history = new History(map);
        var id = map.AllocateObjectId();
        var tree = Image(id, TestMaps.Tile(1));

        history.Do(MapEdits.InsertObject(spawns, tree, 0));
        history.Do(MapEdits.ReplaceObject(spawns, tree.MoveTo(new Vector2(300, 100), map.Layout) with { Name = "Oak" }));
        history.Do(MapEdits.InsertObject(spawns, Image(map.AllocateObjectId(), TestMaps.Tile(2)), 1));
        history.Do(MapEdits.ReorderObject(spawns, id, 1));
        history.Do(MapEdits.MoveObject(spawns, props, id, 0));

        Assert.Equal([2], spawns.Objects.Select(o => o.Id));
        var moved = Assert.Single(props.Objects);
        Assert.Equal(("Oak", map.Layout.WorldToCell(new Vector2(300, 100))), (moved.Name, moved.Cell));

        history.Undo();
        Assert.Equal([2, 1], spawns.Objects.Select(o => o.Id));
        history.UndoAll();
        Assert.Empty(spawns.Objects);
        history.RedoAll();
        Assert.Equal("Oak", props.Objects[0].Name);
    }

    [Fact]
    public void MapSettingsUndo()
    {
        var map = TestMaps.Create();
        var history = new History(map);

        history.Do(MapEdits.ChangeMap(new MapSettings(map.Properties.With("title", PropertyValue.FromString("Isles")), Color.Black)));

        Assert.Equal("Isles", map.Properties.GetString("title"));
        history.Undo();
        Assert.Equal((0, (Color?)null), (map.Properties.Count, map.BackgroundColor));
    }

    [Fact]
    public void RepeatedUndoAndRedoKeepEditsTheSameSize()
    {
        var map = TestMaps.Create();
        map.Ground().SetCell(GridCoord.Zero, TestMaps.Tile(1));
        IMapEdit edit = MapEdits.RemoveTileset(map, map.Tilesets[0]);
        var size = edit.EstimatedSize;

        for (var i = 0; i < 10; i++)
            edit = edit.Apply(map);

        Assert.Equal(size, edit.EstimatedSize);
    }

    [Fact]
    public void EditsOfMissingTargetsFail()
    {
        var map = TestMaps.Create();
        var layer = new ObjectLayer("Loose");

        Assert.Throws<InvalidOperationException>(() => MapEdits.RemoveLayer(layer).Apply(map));
        Assert.Throws<InvalidOperationException>(() => MapEdits.ReplaceTerrain(new Terrain("Ghost", 1, 0, [], id: 9)).Apply(map));
    }

    private static MapObject Image(int id, TileCell tile) =>
        new(id, string.Empty, "prop", MapObjectShape.Tile, default, GridCoord.Zero, [], tile, PropertySet.Empty);

    /// <summary>The smallest undo stack: each step keeps the edit that reverts it.</summary>
    private sealed class History(TileMap map)
    {
        private readonly Stack<IMapEdit> _undo = new();
        private readonly Stack<IMapEdit> _redo = new();

        public void Do(IMapEdit edit)
        {
            _undo.Push(edit.Apply(map));
            _redo.Clear();
        }

        public void Undo() => _redo.Push(_undo.Pop().Apply(map));

        public void UndoAll()
        {
            while (_undo.Count > 0)
                Undo();
        }

        public void RedoAll()
        {
            while (_redo.Count > 0)
                _undo.Push(_redo.Pop().Apply(map));
        }
    }
}
