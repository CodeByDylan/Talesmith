using System.Numerics;
using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Maps;

/// <summary>A tile map: its grid, tilesets, terrains, layers and properties, independent of the format it came from.</summary>
/// <remarks>
/// Maps can be changed while the game runs and while an editor works on them. Every change raises <see cref="Changed"/> and bumps
/// <see cref="Version"/>; cell changes also bump the <see cref="TileChunk.Version"/> of their chunk, and tileset changes bump
/// <see cref="TilesetVersion"/>, so renderers and colliders can poll instead of subscribing. Undoable edits live in
/// <see cref="Editing"/>. Maps are not thread-safe: change them on the game thread.
/// </remarks>
public sealed class TileMap
{
    /// <summary>The chunk size of new maps as log2: 32 cells per side, like Hexy.</summary>
    public const int DefaultChunkShift = 5;

    private readonly List<MapLayer> _layers = [];
    private readonly List<Tileset> _tilesets = [];
    private readonly Dictionary<int, Tileset> _tilesetsById = new();
    private readonly List<Terrain> _terrains = [];
    private TileLayer[] _tileLayers = [];
    private ObjectLayer[] _objectLayers = [];
    private PropertySet _properties;
    private Color? _backgroundColor;
    private int _nextObjectId = 1;

    public TileMap(string path, IGridLayout layout, int chunkShift, IReadOnlyList<Tileset> tilesets, IReadOnlyList<MapLayer> layers, PropertySet properties,
        IReadOnlyList<Terrain>? terrains = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tilesets);
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkShift, 1);
        Path = path;
        Layout = layout;
        ChunkShift = chunkShift;
        _properties = properties;
        foreach (var tileset in tilesets)
            AttachTileset(_tilesets.Count, tileset);
        foreach (var terrain in terrains ?? [])
            AttachTerrain(_terrains.Count, terrain);
        foreach (var layer in layers)
            AttachLayer(_layers.Count, layer);
        RefreshLayerViews();
        foreach (var objects in _objectLayers)
        {
            foreach (var mapObject in objects.Objects)
                ReserveObjectId(mapObject.Id);
        }
    }

    /// <summary>The asset path the map was loaded from, or the path it is saved to.</summary>
    public string Path { get; set; }

    /// <summary>The grid, fixed for the map's life because tile rotations, terrain rules and object positions depend on it.</summary>
    public IGridLayout Layout { get; }

    /// <summary>Log2 of the chunk size shared by every tile layer.</summary>
    public int ChunkShift { get; }

    public IReadOnlyList<Tileset> Tilesets => _tilesets;

    /// <summary>The layers, back to front.</summary>
    public IReadOnlyList<MapLayer> Layers => _layers;

    /// <summary>The terrains painted with automatic transitions.</summary>
    public IReadOnlyList<Terrain> Terrains => _terrains;

    public PropertySet Properties
    {
        get => _properties;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_properties, value))
                return;
            _properties = value;
            RaiseChanged(new MapChange(MapChangeKind.Map));
        }
    }

    public Color? BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            if (_backgroundColor == value)
                return;
            _backgroundColor = value;
            RaiseChanged(new MapChange(MapChangeKind.Map));
        }
    }

    /// <summary>The id the next new object receives; always above every object id in the map.</summary>
    public int NextObjectId
    {
        get => _nextObjectId;
        set => _nextObjectId = Math.Max(1, value);
    }

    /// <summary>Data the format the map was read from keeps to write it back faithfully; editors leave it alone.</summary>
    public object? FormatData { get; set; }

    /// <summary>Increases with every change to the map.</summary>
    public long Version { get; private set; }

    /// <summary>Increases whenever a tileset is added, removed or changed, including its tiles' data, so cached tile geometry can be rebuilt.</summary>
    public long TilesetVersion { get; private set; }

    /// <summary>The rotation, in degrees, of one step of <see cref="TileCell.Rotation"/>: 60 on hex grids and 90 on square grids.</summary>
    public float RotationStepDegrees => 360f / Layout.RotationSteps;

    /// <summary>The tile layers, back to front; a new list after layers are added, removed or moved.</summary>
    public IReadOnlyList<TileLayer> TileLayers => _tileLayers;

    /// <summary>The object layers, back to front; a new list after layers are added, removed or moved.</summary>
    public IReadOnlyList<ObjectLayer> ObjectLayers => _objectLayers;

    /// <summary>Raised after every change, on the thread that made it.</summary>
    public event EventHandler<MapChange>? Changed;

    /// <summary>Creates an empty map with one tile layer named "Ground".</summary>
    /// <param name="cellWidth">The width of a cell's bounding box; for hexes, the distance between flat sides on pointy-top grids.</param>
    public static TileMap Create(GridKind kind, float cellWidth, float cellHeight, int chunkShift = DefaultChunkShift, string path = "")
    {
        IGridLayout layout = kind == GridKind.Square ? new SquareLayout(cellWidth, cellHeight) : new HexLayout(kind == GridKind.HexPointyTop, cellWidth, cellHeight);
        return new TileMap(path, layout, chunkShift, [], [new TileLayer("Ground", chunkShift)], PropertySet.Empty);
    }

    public Tileset? FindTileset(int id) => _tilesetsById.GetValueOrDefault(id);

    public MapLayer? FindLayer(string name)
    {
        foreach (var layer in _layers)
        {
            if (string.Equals(layer.Name, name, StringComparison.Ordinal))
                return layer;
        }

        return null;
    }

    public MapLayer? FindLayer(Guid id)
    {
        foreach (var layer in _layers)
        {
            if (layer.Id == id)
                return layer;
        }

        return null;
    }

    public int IndexOf(MapLayer layer) => _layers.IndexOf(layer);

    /// <summary>Creates a tile layer with this map's chunk size; add it with <see cref="InsertLayer"/>.</summary>
    public TileLayer CreateTileLayer(string name, LayerRole role = LayerRole.Ground) => new(name, ChunkShift, role);

    /// <summary>Makes a name like "Layer 3" that no other layer uses.</summary>
    public string UniqueLayerName(string baseName)
    {
        if (!HasLayerNamed(baseName))
            return baseName;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!HasLayerNamed(candidate))
                return candidate;
        }
    }

    public void AddLayer(MapLayer layer) => InsertLayer(_layers.Count, layer);

    /// <exception cref="InvalidOperationException">The layer belongs to a map, or is a tile layer with another chunk size.</exception>
    public void InsertLayer(int index, MapLayer layer)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_layers.Count, nameof(index));
        AttachLayer(index, layer);
        RefreshLayerViews();
        if (layer is ObjectLayer objects)
        {
            foreach (var mapObject in objects.Objects)
                ReserveObjectId(mapObject.Id);
        }

        RaiseChanged(new MapChange(MapChangeKind.LayerAdded, layer, Index: index));
    }

    /// <summary>Removes a layer and returns the index it had, or -1 when it is not part of this map.</summary>
    public int RemoveLayer(MapLayer layer)
    {
        var index = _layers.IndexOf(layer);
        if (index < 0)
            return -1;
        _layers.RemoveAt(index);
        layer.Map = null;
        RefreshLayerViews();
        RaiseChanged(new MapChange(MapChangeKind.LayerRemoved, layer, Index: index));
        return index;
    }

    public void MoveLayer(int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)fromIndex, (uint)_layers.Count, nameof(fromIndex));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)toIndex, (uint)_layers.Count, nameof(toIndex));
        if (fromIndex == toIndex)
            return;
        var layer = _layers[fromIndex];
        _layers.RemoveAt(fromIndex);
        _layers.Insert(toIndex, layer);
        RefreshLayerViews();
        RaiseChanged(new MapChange(MapChangeKind.LayerMoved, layer, Index: toIndex, PreviousIndex: fromIndex));
    }

    public void AddTileset(Tileset tileset) => InsertTileset(_tilesets.Count, tileset);

    /// <summary>Adds a tileset, giving it a free id when its id is 0 or taken.</summary>
    /// <exception cref="InvalidOperationException">The tileset belongs to a map, or the map has no free tileset id.</exception>
    public void InsertTileset(int index, Tileset tileset)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_tilesets.Count, nameof(index));
        AttachTileset(index, tileset);
        RaiseChanged(new MapChange(MapChangeKind.TilesetAdded, Tileset: tileset, Index: index));
    }

    /// <summary>Removes a tileset and returns the index it had, or -1; cells that use it stay, see <see cref="Editing.MapEdits.RemoveTileset"/>.</summary>
    public int RemoveTileset(Tileset tileset)
    {
        var index = _tilesets.IndexOf(tileset);
        if (index < 0)
            return -1;
        _tilesets.RemoveAt(index);
        _tilesetsById.Remove(tileset.Id);
        tileset.Map = null;
        RaiseChanged(new MapChange(MapChangeKind.TilesetRemoved, Tileset: tileset, Index: index));
        return index;
    }

    public Terrain? FindTerrain(int id)
    {
        foreach (var terrain in _terrains)
        {
            if (terrain.Id == id)
                return terrain;
        }

        return null;
    }

    /// <summary>Finds the terrain that produces a tile.</summary>
    public Terrain? FindTerrain(TileCell tile)
    {
        if (tile.IsEmpty)
            return null;
        foreach (var terrain in _terrains)
        {
            if (terrain.Contains(tile))
                return terrain;
        }

        return null;
    }

    public void AddTerrain(Terrain terrain) => InsertTerrain(_terrains.Count, terrain);

    /// <summary>Adds a terrain, giving it a free id when its id is 0 or taken.</summary>
    public void InsertTerrain(int index, Terrain terrain)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_terrains.Count, nameof(index));
        AttachTerrain(index, terrain);
        RaiseChanged(new MapChange(MapChangeKind.Terrains, Index: index));
    }

    /// <summary>Removes a terrain and returns the index it had, or -1.</summary>
    public int RemoveTerrain(Terrain terrain)
    {
        var index = _terrains.IndexOf(terrain);
        if (index < 0)
            return -1;
        _terrains.RemoveAt(index);
        RaiseChanged(new MapChange(MapChangeKind.Terrains, Index: index));
        return index;
    }

    /// <summary>Replaces the terrain with the same id and returns the previous one.</summary>
    /// <exception cref="ArgumentException">The map has no terrain with that id.</exception>
    public Terrain ReplaceTerrain(Terrain replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var index = _terrains.FindIndex(t => t.Id == replacement.Id);
        if (index < 0)
            throw new ArgumentException($"The map has no terrain {replacement.Id}.", nameof(replacement));
        var previous = _terrains[index];
        if (ReferenceEquals(previous, replacement))
            return previous;
        _terrains[index] = replacement;
        RaiseChanged(new MapChange(MapChangeKind.Terrains, Index: index));
        return previous;
    }

    /// <summary>Reserves a new map-unique object id.</summary>
    public int AllocateObjectId() => _nextObjectId++;

    /// <summary>Finds the topmost tile at a cell across visible tile layers.</summary>
    public TileCell TopTileAt(GridCoord cell)
    {
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            if (_layers[i] is TileLayer { IsVisible: true } layer && layer.GetCell(cell) is { IsEmpty: false } tile)
                return tile;
        }

        return TileCell.Empty;
    }

    /// <summary>The world bounds of every non-empty cell; decodes chunks, so maps with more than <paramref name="maxChunks"/> chunks fall back to <see cref="WorldBounds"/>.</summary>
    public Rect2 MeasureContentBounds(int maxChunks = 256)
    {
        var chunkCount = 0;
        foreach (var layer in _tileLayers)
            chunkCount += layer.Chunks.Count;
        if (chunkCount > maxChunks)
            return WorldBounds;

        var bounds = Rect2.Empty;
        foreach (var layer in _tileLayers)
        {
            foreach (var placed in layer.Cells)
                bounds = bounds.Union(Layout.CellBounds(placed.Cell));
        }

        return bounds;
    }

    /// <summary>The world bounds covered by the map's chunks, accurate to one chunk.</summary>
    public Rect2 WorldBounds
    {
        get
        {
            var bounds = Rect2.Empty;
            foreach (var layer in _tileLayers)
            {
                var last = layer.ChunkSize - 1;
                foreach (var coord in layer.Chunks.Keys)
                {
                    var origin = coord.Origin(ChunkShift);
                    Span<GridCoord> corners = [origin, new(origin.X + last, origin.Y), new(origin.X, origin.Y + last), new(origin.X + last, origin.Y + last)];
                    foreach (var corner in corners)
                        bounds = bounds.Union(Layout.CellBounds(corner));
                }
            }

            return bounds;
        }
    }

    /// <summary>Converts a world position, relative to the center of cell (0, 0), to its cell and the offset from that cell in cell units.</summary>
    public (GridCoord Cell, Vector2 Offset) Anchor(Vector2 position)
    {
        var continuous = Layout.WorldToContinuous(position);
        var cell = Layout.Topology.Round(continuous);
        return (cell, continuous - new Vector2(cell.X, cell.Y));
    }

    internal void ReserveObjectId(int id)
    {
        if (id >= _nextObjectId)
            _nextObjectId = id + 1;
    }

    internal void RaiseChanged(in MapChange change)
    {
        Version++;
        if (change.Kind is MapChangeKind.TilesetAdded or MapChangeKind.TilesetRemoved or MapChangeKind.TilesetChanged)
            TilesetVersion++;
        Changed?.Invoke(this, change);
    }

    private bool HasLayerNamed(string name)
    {
        foreach (var layer in _layers)
        {
            if (string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void AttachLayer(int index, MapLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Map is not null)
            throw new InvalidOperationException($"Layer \"{layer.Name}\" already belongs to a map.");
        if (layer is TileLayer tiles && tiles.ChunkShift != ChunkShift)
            throw new InvalidOperationException($"Layer \"{layer.Name}\" uses chunks of {tiles.ChunkSize} cells but the map uses {1 << ChunkShift}.");
        _layers.Insert(index, layer);
        layer.Map = this;
    }

    private void AttachTileset(int index, Tileset tileset)
    {
        ArgumentNullException.ThrowIfNull(tileset);
        if (tileset.Map is not null)
            throw new InvalidOperationException($"Tileset \"{tileset.Name}\" already belongs to a map.");
        if (tileset.Id is < 1 or > TileCell.MaxTilesetId || _tilesetsById.ContainsKey(tileset.Id))
            tileset.Id = FreeTilesetId();
        _tilesets.Insert(index, tileset);
        _tilesetsById.Add(tileset.Id, tileset);
        tileset.Map = this;
    }

    private void AttachTerrain(int index, Terrain terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (terrain.Id == 0 || FindTerrain(terrain.Id) is not null)
        {
            var max = 0;
            foreach (var other in _terrains)
                max = Math.Max(max, other.Id);
            terrain.Id = max + 1;
        }

        _terrains.Insert(index, terrain);
    }

    private int FreeTilesetId()
    {
        for (var id = 1; id <= TileCell.MaxTilesetId; id++)
        {
            if (!_tilesetsById.ContainsKey(id))
                return id;
        }

        throw new InvalidOperationException($"A map cannot hold more than {TileCell.MaxTilesetId} tilesets.");
    }

    private void RefreshLayerViews()
    {
        var tileLayers = new List<TileLayer>(_layers.Count);
        var objectLayers = new List<ObjectLayer>();
        foreach (var layer in _layers)
        {
            if (layer is TileLayer tiles)
                tileLayers.Add(tiles);
            else if (layer is ObjectLayer objects)
                objectLayers.Add(objects);
        }

        _tileLayers = [.. tileLayers];
        _objectLayers = [.. objectLayers];
    }
}
