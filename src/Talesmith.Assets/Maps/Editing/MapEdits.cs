using Talesmith.Assets.Textures;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>The editable settings of a layer, applied together by <see cref="MapEdits.ChangeLayer"/>.</summary>
/// <param name="Color">The drawing color of object layers; ignored by tile layers.</param>
public sealed record LayerSettings(string Name, LayerRole Role, bool IsVisible, bool IsLocked, float Opacity, PropertySet Properties, Color? Color = null)
{
    public static LayerSettings Of(MapLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new LayerSettings(layer.Name, layer.Role, layer.IsVisible, layer.IsLocked, layer.Opacity, layer.Properties, (layer as ObjectLayer)?.Color);
    }

    internal void ApplyTo(MapLayer layer)
    {
        layer.Name = Name;
        layer.Role = Role;
        layer.IsVisible = IsVisible;
        layer.IsLocked = IsLocked;
        layer.Opacity = Opacity;
        layer.Properties = Properties;
        if (layer is ObjectLayer objects)
            objects.Color = Color;
    }
}

/// <summary>The editable settings of a tileset, applied together by <see cref="MapEdits.ChangeTileset"/>.</summary>
public sealed record TilesetSettings(string Name, int TileWidth, int TileHeight, int Margin, int Spacing, int Columns, int TileCount, TilePlacement Placement,
    TextureAsset? Texture, TilesetImageFile? ImageFile)
{
    public static TilesetSettings Of(Tileset tileset)
    {
        ArgumentNullException.ThrowIfNull(tileset);
        return new TilesetSettings(tileset.Name, tileset.TileWidth, tileset.TileHeight, tileset.Margin, tileset.Spacing, tileset.Columns, tileset.TileCount,
            tileset.Placement, tileset.Texture, tileset.ImageFile);
    }

    internal void ApplyTo(Tileset tileset)
    {
        tileset.Name = Name;
        tileset.TileWidth = TileWidth;
        tileset.TileHeight = TileHeight;
        tileset.Margin = Margin;
        tileset.Spacing = Spacing;
        tileset.Columns = Columns;
        tileset.TileCount = TileCount;
        tileset.Placement = Placement;
        tileset.Texture = Texture;
        tileset.ImageFile = ImageFile;
    }
}

/// <summary>The editable settings of a map, applied together by <see cref="MapEdits.ChangeMap"/>.</summary>
public sealed record MapSettings(PropertySet Properties, Color? BackgroundColor)
{
    public static MapSettings Of(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return new MapSettings(map.Properties, map.BackgroundColor);
    }
}

/// <summary>Creates the reversible edits of layers, tilesets, terrains, objects and map settings; cells are edited with <see cref="TileEdit"/>.</summary>
/// <remarks>Creating an edit changes nothing; <see cref="IMapEdit.Apply"/> does, and returns the edit that reverts it.</remarks>
public static class MapEdits
{
    public static IMapEdit InsertLayer(MapLayer layer, int index) => new InsertLayerEdit(layer ?? throw new ArgumentNullException(nameof(layer)), index);

    public static IMapEdit RemoveLayer(MapLayer layer) => new RemoveLayerEdit(layer ?? throw new ArgumentNullException(nameof(layer)));

    public static IMapEdit MoveLayer(MapLayer layer, int toIndex) => new MoveLayerEdit(layer ?? throw new ArgumentNullException(nameof(layer)), toIndex);

    /// <summary>Changes a layer's name, role, visibility, lock, opacity, properties and color in one step.</summary>
    public static IMapEdit ChangeLayer(MapLayer layer, LayerSettings settings) =>
        new ChangeLayerEdit(layer ?? throw new ArgumentNullException(nameof(layer)), settings ?? throw new ArgumentNullException(nameof(settings)));

    public static IMapEdit InsertTileset(Tileset tileset, int index) => new InsertTilesetEdit(tileset ?? throw new ArgumentNullException(nameof(tileset)), index);

    /// <summary>Removes a tileset together with every cell, tile object and terrain that uses it, as Hexy does.</summary>
    public static IMapEdit RemoveTileset(TileMap map, Tileset tileset)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(tileset);
        var edits = new List<IMapEdit>();
        var cells = new List<LayerCellChanges>();
        foreach (var layer in map.TileLayers)
        {
            var changes = new List<CellChange>();
            foreach (var placed in layer.Cells)
            {
                if (placed.Tile.TilesetId == tileset.Id)
                    changes.Add(new CellChange(placed.Cell, placed.Tile, TileCell.Empty));
            }

            if (changes.Count > 0)
                cells.Add(new LayerCellChanges(layer, [.. changes]));
        }

        if (cells.Count > 0)
            edits.Add(new TileChangeSet([.. cells]));

        foreach (var layer in map.ObjectLayers)
        {
            foreach (var mapObject in layer.Objects)
            {
                if (mapObject.Shape == MapObjectShape.Tile && mapObject.Tile.TilesetId == tileset.Id)
                    edits.Add(new RemoveObjectEdit(layer, mapObject.Id));
            }
        }

        foreach (var terrain in map.Terrains)
        {
            if (terrain.TilesetId == tileset.Id)
                edits.Add(new RemoveTerrainEdit(terrain));
        }

        edits.Add(new RemoveTilesetEdit(tileset));
        return edits.Count == 1 ? edits[0] : new CompositeEdit(edits);
    }

    /// <summary>Changes a tileset's name, tile size, atlas layout, placement and image in one step.</summary>
    public static IMapEdit ChangeTileset(Tileset tileset, TilesetSettings settings) =>
        new ChangeTilesetEdit(tileset ?? throw new ArgumentNullException(nameof(tileset)), settings ?? throw new ArgumentNullException(nameof(settings)));

    /// <summary>Replaces the data of one tile, such as its name, color, animation, properties or collision shapes; null clears it.</summary>
    public static IMapEdit ChangeTile(Tileset tileset, int tileId, TileInfo? info) =>
        new ChangeTileEdit(tileset ?? throw new ArgumentNullException(nameof(tileset)), tileId, info);

    public static IMapEdit InsertTerrain(Terrain terrain, int index) => new InsertTerrainEdit(terrain ?? throw new ArgumentNullException(nameof(terrain)), index);

    public static IMapEdit RemoveTerrain(Terrain terrain) => new RemoveTerrainEdit(terrain ?? throw new ArgumentNullException(nameof(terrain)));

    /// <summary>Replaces the terrain with the same id, such as after renaming it or changing its rules.</summary>
    public static IMapEdit ReplaceTerrain(Terrain replacement) => new ReplaceTerrainEdit(replacement ?? throw new ArgumentNullException(nameof(replacement)));

    /// <summary>Adds an object; give new objects an id from <see cref="TileMap.AllocateObjectId"/>.</summary>
    public static IMapEdit InsertObject(ObjectLayer layer, MapObject mapObject, int index) =>
        new InsertObjectEdit(layer ?? throw new ArgumentNullException(nameof(layer)), mapObject ?? throw new ArgumentNullException(nameof(mapObject)), index);

    public static IMapEdit RemoveObject(ObjectLayer layer, int objectId) => new RemoveObjectEdit(layer ?? throw new ArgumentNullException(nameof(layer)), objectId);

    /// <summary>Replaces the object with the same id, as moving, reshaping, renaming or editing its properties does.</summary>
    public static IMapEdit ReplaceObject(ObjectLayer layer, MapObject replacement) =>
        new ReplaceObjectEdit(layer ?? throw new ArgumentNullException(nameof(layer)), replacement ?? throw new ArgumentNullException(nameof(replacement)));

    /// <summary>Moves an object within its layer's drawing order.</summary>
    public static IMapEdit ReorderObject(ObjectLayer layer, int objectId, int toIndex) =>
        new ReorderObjectEdit(layer ?? throw new ArgumentNullException(nameof(layer)), objectId, toIndex);

    /// <summary>Moves objects to another layer, keeping their ids.</summary>
    public static IMapEdit MoveObject(ObjectLayer from, ObjectLayer to, int objectId, int toIndex)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var mapObject = from.Find(objectId) ?? throw new ArgumentException($"Layer \"{from.Name}\" has no object {objectId}.", nameof(objectId));
        return new CompositeEdit([new RemoveObjectEdit(from, objectId), new InsertObjectEdit(to, mapObject, toIndex)]);
    }

    public static IMapEdit ChangeMap(MapSettings settings) => new ChangeMapEdit(settings ?? throw new ArgumentNullException(nameof(settings)));

    private static void RequireLayer(TileMap map, MapLayer layer)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (layer.Map != map)
            throw new InvalidOperationException($"Layer \"{layer.Name}\" is not part of the map.");
    }

    private static void RequireTileset(TileMap map, Tileset tileset)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (tileset.Map != map)
            throw new InvalidOperationException($"Tileset \"{tileset.Name}\" is not part of the map.");
    }

    private sealed class InsertLayerEdit(MapLayer layer, int index) : IMapEdit
    {
        public long EstimatedSize => 64;

        public IMapEdit Apply(TileMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            map.InsertLayer(Math.Clamp(index, 0, map.Layers.Count), layer);
            return new RemoveLayerEdit(layer);
        }
    }

    private sealed class RemoveLayerEdit(MapLayer layer) : IMapEdit
    {
        public long EstimatedSize => 64;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            return new InsertLayerEdit(layer, map.RemoveLayer(layer));
        }
    }

    private sealed class MoveLayerEdit(MapLayer layer, int toIndex) : IMapEdit
    {
        public long EstimatedSize => 32;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            var from = map.IndexOf(layer);
            map.MoveLayer(from, Math.Clamp(toIndex, 0, map.Layers.Count - 1));
            return new MoveLayerEdit(layer, from);
        }
    }

    private sealed class ChangeLayerEdit(MapLayer layer, LayerSettings settings) : IMapEdit
    {
        public long EstimatedSize => 128;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            var previous = LayerSettings.Of(layer);
            settings.ApplyTo(layer);
            return new ChangeLayerEdit(layer, previous);
        }
    }

    private sealed class InsertTilesetEdit(Tileset tileset, int index) : IMapEdit
    {
        public long EstimatedSize => 64;

        public IMapEdit Apply(TileMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            map.InsertTileset(Math.Clamp(index, 0, map.Tilesets.Count), tileset);
            return new RemoveTilesetEdit(tileset);
        }
    }

    private sealed class RemoveTilesetEdit(Tileset tileset) : IMapEdit
    {
        public long EstimatedSize => 64;

        public IMapEdit Apply(TileMap map)
        {
            RequireTileset(map, tileset);
            return new InsertTilesetEdit(tileset, map.RemoveTileset(tileset));
        }
    }

    private sealed class ChangeTilesetEdit(Tileset tileset, TilesetSettings settings) : IMapEdit
    {
        public long EstimatedSize => 128;

        public IMapEdit Apply(TileMap map)
        {
            RequireTileset(map, tileset);
            var previous = TilesetSettings.Of(tileset);
            settings.ApplyTo(tileset);
            return new ChangeTilesetEdit(tileset, previous);
        }
    }

    private sealed class ChangeTileEdit(Tileset tileset, int tileId, TileInfo? info) : IMapEdit
    {
        public long EstimatedSize => 128;

        public IMapEdit Apply(TileMap map)
        {
            RequireTileset(map, tileset);
            return new ChangeTileEdit(tileset, tileId, tileset.SetTile(tileId, info));
        }
    }

    private sealed class InsertTerrainEdit(Terrain terrain, int index) : IMapEdit
    {
        public long EstimatedSize => 64;

        public IMapEdit Apply(TileMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            map.InsertTerrain(Math.Clamp(index, 0, map.Terrains.Count), terrain);
            return new RemoveTerrainEdit(terrain);
        }
    }

    private sealed class RemoveTerrainEdit(Terrain terrain) : IMapEdit
    {
        public long EstimatedSize => 64;

        public IMapEdit Apply(TileMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            var index = map.RemoveTerrain(terrain);
            if (index < 0)
                throw new InvalidOperationException($"Terrain \"{terrain.Name}\" is not part of the map.");
            return new InsertTerrainEdit(terrain, index);
        }
    }

    private sealed class ReplaceTerrainEdit(Terrain replacement) : IMapEdit
    {
        public long EstimatedSize => 128;

        public IMapEdit Apply(TileMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            try
            {
                return new ReplaceTerrainEdit(map.ReplaceTerrain(replacement));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
        }
    }

    private sealed class InsertObjectEdit(ObjectLayer layer, MapObject mapObject, int index) : IMapEdit
    {
        public long EstimatedSize => 128;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            layer.Insert(Math.Clamp(index, 0, layer.Objects.Count), mapObject);
            return new RemoveObjectEdit(layer, mapObject.Id);
        }
    }

    private sealed class RemoveObjectEdit(ObjectLayer layer, int objectId) : IMapEdit
    {
        public long EstimatedSize => 32;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            var index = layer.IndexOf(objectId);
            if (index < 0)
                throw new InvalidOperationException($"Layer \"{layer.Name}\" has no object {objectId}.");
            return new InsertObjectEdit(layer, layer.RemoveAt(index), index);
        }
    }

    private sealed class ReplaceObjectEdit(ObjectLayer layer, MapObject replacement) : IMapEdit
    {
        public long EstimatedSize => 128;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            if (layer.IndexOf(replacement.Id) < 0)
                throw new InvalidOperationException($"Layer \"{layer.Name}\" has no object {replacement.Id}.");
            return new ReplaceObjectEdit(layer, layer.Replace(replacement));
        }
    }

    private sealed class ReorderObjectEdit(ObjectLayer layer, int objectId, int toIndex) : IMapEdit
    {
        public long EstimatedSize => 32;

        public IMapEdit Apply(TileMap map)
        {
            RequireLayer(map, layer);
            var from = layer.IndexOf(objectId);
            if (from < 0)
                throw new InvalidOperationException($"Layer \"{layer.Name}\" has no object {objectId}.");
            layer.Move(from, Math.Clamp(toIndex, 0, layer.Objects.Count - 1));
            return new ReorderObjectEdit(layer, objectId, from);
        }
    }

    private sealed class ChangeMapEdit(MapSettings settings) : IMapEdit
    {
        public long EstimatedSize => 96;

        public IMapEdit Apply(TileMap map)
        {
            ArgumentNullException.ThrowIfNull(map);
            var previous = MapSettings.Of(map);
            map.Properties = settings.Properties;
            map.BackgroundColor = settings.BackgroundColor;
            return new ChangeMapEdit(previous);
        }
    }
}
