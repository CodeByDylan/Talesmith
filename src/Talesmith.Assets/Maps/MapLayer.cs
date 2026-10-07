using Talesmith.Grids;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Maps;

/// <summary>A layer of a tile map; layers are drawn in order, first at the back.</summary>
/// <remarks>Changing a layer that belongs to a map raises <see cref="TileMap.Changed"/>. Change maps on the game thread.</remarks>
public abstract class MapLayer
{
    private string _name;
    private LayerRole _role;
    private bool _isVisible = true;
    private bool _isLocked;
    private float _opacity = 1;
    private PropertySet _properties = PropertySet.Empty;

    protected MapLayer(string name, LayerRole role)
    {
        ArgumentNullException.ThrowIfNull(name);
        _name = name;
        _role = role;
    }

    /// <summary>Identifies the layer for its whole life, also across saves.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The map the layer belongs to, or null while it is not part of one.</summary>
    public TileMap? Map { get; internal set; }

    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Set(ref _name, value))
                OnChanged();
        }
    }

    public LayerRole Role
    {
        get => _role;
        set
        {
            if (Set(ref _role, value))
                OnChanged();
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (Set(ref _isVisible, value))
                OnChanged();
        }
    }

    /// <summary>Whether editing tools must leave the layer untouched; the game ignores it.</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (Set(ref _isLocked, value))
                OnChanged();
        }
    }

    /// <summary>From 0, invisible, to 1, opaque.</summary>
    public float Opacity
    {
        get => _opacity;
        set
        {
            if (Set(ref _opacity, Math.Clamp(value, 0, 1)))
                OnChanged();
        }
    }

    public PropertySet Properties
    {
        get => _properties;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Set(ref _properties, value))
                OnChanged();
        }
    }

    /// <summary>Data the format the layer was read from keeps to write it back faithfully; editors leave it alone.</summary>
    public object? FormatData { get; set; }

    public override string ToString() => Name;

    protected void OnChanged() => Map?.RaiseChanged(new MapChange(MapChangeKind.LayerChanged, this));

    protected void CopySettingsTo(MapLayer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target._role = _role;
        target._isVisible = _isVisible;
        target._isLocked = _isLocked;
        target._opacity = _opacity;
        target._properties = _properties;
    }

    private static bool Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        return true;
    }
}

/// <summary>A placed tile and its cell.</summary>
public readonly record struct PlacedTile(GridCoord Cell, TileCell Tile);

/// <summary>A layer of tiles stored in chunks, so only the parts of the map that contain tiles use memory.</summary>
public sealed class TileLayer : MapLayer
{
    private readonly Dictionary<ChunkCoord, TileChunk> _chunks = new();

    public TileLayer(string name, int chunkShift, LayerRole role = LayerRole.Ground) : base(name, role)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkShift, 1);
        ChunkShift = chunkShift;
    }

    /// <summary>Log2 of the chunk size.</summary>
    public int ChunkShift { get; }

    public int ChunkSize => 1 << ChunkShift;

    public IReadOnlyDictionary<ChunkCoord, TileChunk> Chunks => _chunks;

    /// <summary>The chunks, enumerable without allocating; each knows its <see cref="TileChunk.Coord"/>.</summary>
    public Dictionary<ChunkCoord, TileChunk>.ValueCollection ChunkValues => _chunks.Values;

    /// <summary>Every non-empty cell, enumerable without allocating; decodes chunks as it goes.</summary>
    public CellEnumerable Cells => new(this);

    /// <summary>Adds a chunk from its compressed cells; used by importers.</summary>
    public void AddCompressedChunk(ChunkCoord coord, byte[] compressed) => _chunks[coord] = new TileChunk(coord, ChunkSize, compressed, null);

    public TileCell GetCell(GridCoord cell)
    {
        if (!_chunks.TryGetValue(ChunkCoord.Of(cell, ChunkShift), out var chunk))
            return TileCell.Empty;
        var mask = ChunkSize - 1;
        return chunk[cell.X & mask, cell.Y & mask];
    }

    /// <summary>Changes a cell, creating its chunk when needed, and raises <see cref="TileMap.Changed"/> for it.</summary>
    /// <remarks>Editors batch changes with <see cref="Editing.TileEdit"/>, which records undo data and notifies once per operation.</remarks>
    public void SetCell(GridCoord cell, TileCell value)
    {
        if (Exchange(cell, value) != value)
            Map?.RaiseChanged(new MapChange(MapChangeKind.Cells, this, Cells: GridBounds.Of(cell)));
    }

    /// <summary>Sets a cell without notifying and returns the value it replaced.</summary>
    internal TileCell Exchange(GridCoord cell, TileCell value)
    {
        var coord = ChunkCoord.Of(cell, ChunkShift);
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            if (value.IsEmpty)
                return TileCell.Empty;
            chunk = new TileChunk(coord, ChunkSize, null, new TileCell[ChunkSize * ChunkSize]);
            _chunks[coord] = chunk;
        }

        var mask = ChunkSize - 1;
        return chunk.Exchange(cell.X & mask, cell.Y & mask, value);
    }

    /// <summary>The cell bounds of every chunk, or an empty bounds when the layer has none.</summary>
    public GridBounds ChunkBounds
    {
        get
        {
            if (_chunks.Count == 0)
                return new GridBounds(0, 0, -1, -1);
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            foreach (var coord in _chunks.Keys)
            {
                minX = Math.Min(minX, coord.X);
                minY = Math.Min(minY, coord.Y);
                maxX = Math.Max(maxX, coord.X);
                maxY = Math.Max(maxY, coord.Y);
            }

            return new GridBounds(minX << ChunkShift, minY << ChunkShift, ((maxX + 1) << ChunkShift) - 1, ((maxY + 1) << ChunkShift) - 1);
        }
    }

    /// <summary>Creates a copy with a new <see cref="MapLayer.Id"/> and the same cells, settings and properties.</summary>
    public TileLayer Clone(string name)
    {
        var copy = new TileLayer(name, ChunkShift, Role);
        CopySettingsTo(copy);
        foreach (var chunk in _chunks.Values)
        {
            var clone = chunk.TryGetCompressed(out var compressed)
                ? new TileChunk(chunk.Coord, chunk.Size, compressed.ToArray(), null)
                : new TileChunk(chunk.Coord, chunk.Size, null, (TileCell[])chunk.EnsureDecoded().Clone());
            copy._chunks.Add(chunk.Coord, clone);
        }

        return copy;
    }

    /// <summary>Enumerates the non-empty cells of a layer.</summary>
    public readonly struct CellEnumerable(TileLayer layer)
    {
        public CellEnumerator GetEnumerator() => new(layer);
    }

    /// <summary>Walks the non-empty cells chunk by chunk.</summary>
    public struct CellEnumerator
    {
        private Dictionary<ChunkCoord, TileChunk>.ValueCollection.Enumerator _chunks;
        private readonly int _shift;
        private TileCell[]? _cells;
        private GridCoord _origin;
        private int _index;

        internal CellEnumerator(TileLayer layer)
        {
            _chunks = layer._chunks.Values.GetEnumerator();
            _shift = layer.ChunkShift;
            _cells = null;
            _index = 0;
        }

        public PlacedTile Current { get; private set; }

        public bool MoveNext()
        {
            while (true)
            {
                if (_cells is not null)
                {
                    while (_index < _cells.Length)
                    {
                        var i = _index++;
                        if (!_cells[i].IsEmpty)
                        {
                            var mask = (1 << _shift) - 1;
                            Current = new PlacedTile(new GridCoord(_origin.X + (i & mask), _origin.Y + (i >> _shift)), _cells[i]);
                            return true;
                        }
                    }
                }

                if (!_chunks.MoveNext())
                    return false;
                var chunk = _chunks.Current;
                _cells = chunk.EnsureDecoded();
                _origin = chunk.Coord.Origin(_shift);
                _index = 0;
            }
        }
    }
}

/// <summary>A layer of free-form objects such as spawn points, trigger areas and placed images.</summary>
public sealed class ObjectLayer : MapLayer
{
    /// <summary>The color Hexy gives new object layers.</summary>
    public static readonly Color DefaultColor = new(0xF5, 0x9E, 0x0B);

    private readonly List<MapObject> _objects;
    private Color? _color;

    public ObjectLayer(string name) : this(name, [])
    {
    }

    public ObjectLayer(string name, IEnumerable<MapObject> objects) : base(name, LayerRole.Object)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _objects = [.. objects];
    }

    /// <summary>The objects in drawing order.</summary>
    public IReadOnlyList<MapObject> Objects => _objects;

    /// <summary>The color used to draw the layer's points and areas in tools and debug views.</summary>
    public Color? Color
    {
        get => _color;
        set
        {
            if (_color == value)
                return;
            _color = value;
            OnChanged();
        }
    }

    public int IndexOf(int objectId)
    {
        for (var i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].Id == objectId)
                return i;
        }

        return -1;
    }

    public MapObject? Find(int objectId) => IndexOf(objectId) is >= 0 and var index ? _objects[index] : null;

    public void Add(MapObject mapObject) => Insert(_objects.Count, mapObject);

    public void Insert(int index, MapObject mapObject)
    {
        ArgumentNullException.ThrowIfNull(mapObject);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)_objects.Count, nameof(index));
        _objects.Insert(index, mapObject);
        if (Map is { } map)
        {
            map.ReserveObjectId(mapObject.Id);
            map.RaiseChanged(new MapChange(MapChangeKind.Objects, this, Index: index, ObjectId: mapObject.Id));
        }
    }

    /// <summary>Removes the object at <paramref name="index"/> and returns it.</summary>
    public MapObject RemoveAt(int index)
    {
        var removed = _objects[index];
        _objects.RemoveAt(index);
        Map?.RaiseChanged(new MapChange(MapChangeKind.Objects, this, Index: index, ObjectId: removed.Id));
        return removed;
    }

    /// <summary>Replaces the object with the same id, as moving or editing an object does, and returns the previous version.</summary>
    /// <exception cref="ArgumentException">The layer has no object with that id.</exception>
    public MapObject Replace(MapObject mapObject)
    {
        ArgumentNullException.ThrowIfNull(mapObject);
        var index = IndexOf(mapObject.Id);
        if (index < 0)
            throw new ArgumentException($"Layer \"{Name}\" has no object {mapObject.Id}.", nameof(mapObject));
        var previous = _objects[index];
        if (previous == mapObject)
            return previous;
        _objects[index] = mapObject;
        Map?.RaiseChanged(new MapChange(MapChangeKind.Objects, this, Index: index, ObjectId: mapObject.Id));
        return previous;
    }

    /// <summary>Moves an object within the drawing order.</summary>
    public void Move(int fromIndex, int toIndex)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)fromIndex, (uint)_objects.Count, nameof(fromIndex));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)toIndex, (uint)_objects.Count, nameof(toIndex));
        if (fromIndex == toIndex)
            return;
        var mapObject = _objects[fromIndex];
        _objects.RemoveAt(fromIndex);
        _objects.Insert(toIndex, mapObject);
        Map?.RaiseChanged(new MapChange(MapChangeKind.Objects, this, Index: toIndex, PreviousIndex: fromIndex, ObjectId: mapObject.Id));
    }

    /// <summary>Creates a copy with a new <see cref="MapLayer.Id"/>; objects keep their ids, so give them new ones before adding it to the same map.</summary>
    public ObjectLayer Clone(string name)
    {
        var copy = new ObjectLayer(name, _objects) { _color = _color };
        CopySettingsTo(copy);
        return copy;
    }
}
