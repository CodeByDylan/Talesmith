using System.Runtime.CompilerServices;
using Talesmith.Grids;

namespace Talesmith.Assets.Maps.Editing;

/// <summary>One cell's value before and after an edit.</summary>
public readonly record struct CellChange(GridCoord Cell, TileCell Old, TileCell New);

/// <summary>The changed cells of one layer.</summary>
public sealed class LayerCellChanges(TileLayer layer, CellChange[] changes)
{
    public TileLayer Layer { get; } = layer;

    public IReadOnlyList<CellChange> Changes => changes;

    internal CellChange[] Items => changes;

    /// <summary>The bounds of every changed cell.</summary>
    public GridBounds Bounds
    {
        get
        {
            var bounds = GridBounds.Empty;
            foreach (var change in changes)
                bounds = bounds.Include(change.Cell);
            return bounds;
        }
    }
}

/// <summary>Cell changes on any number of layers, stored as old and new values per cell: the undo record of a <see cref="TileEdit"/>.</summary>
public sealed class TileChangeSet : IMapEdit
{
    private readonly LayerCellChanges[] _layers;

    internal TileChangeSet(LayerCellChanges[] layers)
    {
        _layers = layers;
        foreach (var layer in layers)
            Count += layer.Items.Length;
    }

    public IReadOnlyList<LayerCellChanges> Layers => _layers;

    /// <summary>The number of changed cells.</summary>
    public int Count { get; }

    public long EstimatedSize => 64 + _layers.Length * 48L + Count * 16L;

    /// <summary>Gets the change set that writes the old values back, without applying anything.</summary>
    /// <remarks>A committed <see cref="TileEdit"/> is already applied, so its undo step is <c>Commit()?.Invert()</c>.</remarks>
    public TileChangeSet Invert()
    {
        var inverse = new LayerCellChanges[_layers.Length];
        for (var l = 0; l < _layers.Length; l++)
        {
            var changes = _layers[l].Items;
            var reverted = new CellChange[changes.Length];
            for (var i = 0; i < changes.Length; i++)
                reverted[i] = new CellChange(changes[i].Cell, changes[i].New, changes[i].Old);
            inverse[l] = new LayerCellChanges(_layers[l].Layer, reverted);
        }

        return new TileChangeSet(inverse);
    }

    /// <summary>Writes the new value of every cell, notifying once per layer, and returns the change set that writes the old values back.</summary>
    public IMapEdit Apply(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var inverse = new LayerCellChanges[_layers.Length];
        for (var l = 0; l < _layers.Length; l++)
        {
            var layer = _layers[l].Layer;
            if (layer.Map != map)
                throw new InvalidOperationException($"Layer \"{layer.Name}\" is not part of the map.");

            var changes = _layers[l].Items;
            var reverted = new CellChange[changes.Length];
            var bounds = GridBounds.Empty;
            for (var i = 0; i < changes.Length; i++)
            {
                var change = changes[i];
                var previous = layer.Exchange(change.Cell, change.New);
                if (previous != change.New)
                    bounds = bounds.Include(change.Cell);
                reverted[i] = new CellChange(change.Cell, change.New, change.Old);
            }

            inverse[l] = new LayerCellChanges(layer, reverted);
            if (!bounds.IsEmpty)
                map.RaiseChanged(new MapChange(MapChangeKind.Cells, layer, Cells: bounds));
        }

        return new TileChangeSet(inverse);
    }
}

/// <summary>Changes cells immediately while recording what they held before, then turns into a single undoable <see cref="TileChangeSet"/>.</summary>
/// <remarks>
/// Painting tools write through one edit per stroke, so the map shows every change at once and the whole stroke undoes in one step.
/// Each call notifies <see cref="TileMap.Changed"/> once with the bounds of the cells it changed. Edits may span several layers of
/// the same map.
/// </remarks>
public sealed class TileEdit(TileMap map)
{
    private readonly Dictionary<TileLayer, LayerLog> _logs = new(ReferenceEqualityComparer.Instance);
    private bool _completed;

    public TileMap Map { get; } = map ?? throw new ArgumentNullException(nameof(map));

    /// <summary>The number of distinct cells written so far.</summary>
    public int Count { get; private set; }

    public bool IsCompleted => _completed;

    /// <summary>Writes one cell and returns the value it held.</summary>
    public TileCell Set(TileLayer layer, GridCoord cell, TileCell value)
    {
        var log = Log(layer);
        var previous = Write(layer, log, cell, value);
        Notify(layer, previous != value ? GridBounds.Of(cell) : GridBounds.Empty);
        return previous;
    }

    /// <summary>Writes the same value to every cell.</summary>
    public void Fill(TileLayer layer, CellSet cells, TileCell value)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var log = Log(layer);
        var bounds = GridBounds.Empty;
        foreach (var cell in cells)
        {
            if (Write(layer, log, cell, value) != value)
                bounds = bounds.Include(cell);
        }

        Notify(layer, bounds);
    }

    /// <summary>Writes the same value to every cell.</summary>
    [OverloadResolutionPriority(1)]
    public void Fill(TileLayer layer, ReadOnlySpan<GridCoord> cells, TileCell value)
    {
        var log = Log(layer);
        var bounds = GridBounds.Empty;
        foreach (var cell in cells)
        {
            if (Write(layer, log, cell, value) != value)
                bounds = bounds.Include(cell);
        }

        Notify(layer, bounds);
    }

    /// <summary>Writes the brush's tile for each cell.</summary>
    public void Paint(TileLayer layer, CellSet cells, TileBrush brush)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(brush);
        var log = Log(layer);
        var bounds = GridBounds.Empty;
        foreach (var cell in cells)
        {
            var value = brush.TileFor(cell);
            if (Write(layer, log, cell, value) != value)
                bounds = bounds.Include(cell);
        }

        Notify(layer, bounds);
    }

    /// <summary>Writes a value per cell.</summary>
    public void SetMany(TileLayer layer, ReadOnlySpan<PlacedTile> cells)
    {
        var log = Log(layer);
        var bounds = GridBounds.Empty;
        foreach (var placed in cells)
        {
            if (Write(layer, log, placed.Cell, placed.Tile) != placed.Tile)
                bounds = bounds.Include(placed.Cell);
        }

        Notify(layer, bounds);
    }

    /// <summary>Writes a value per cell.</summary>
    public void SetMany(TileLayer layer, IEnumerable<KeyValuePair<GridCoord, TileCell>> cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var log = Log(layer);
        var bounds = GridBounds.Empty;
        foreach (var (cell, value) in cells)
        {
            if (Write(layer, log, cell, value) != value)
                bounds = bounds.Include(cell);
        }

        Notify(layer, bounds);
    }

    /// <summary>Gets the value a cell held before this edit first wrote it.</summary>
    public TileCell Original(TileLayer layer, GridCoord cell)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return _logs.TryGetValue(layer, out var log) && log.Indices.TryGetValue(cell, out var index) ? log.Changes[index].Old : layer.GetCell(cell);
    }

    /// <summary>Finishes the edit and returns the changes it made, which are already applied, or null when no cell ended up different.</summary>
    /// <remarks>Undo stacks keep <see cref="TileChangeSet.Invert"/> of the result.</remarks>
    public TileChangeSet? Commit()
    {
        EnsureActive();
        _completed = true;
        var layers = new List<LayerCellChanges>(_logs.Count);
        foreach (var (layer, log) in _logs)
        {
            var effective = 0;
            foreach (var change in log.Changes)
            {
                if (change.Old != change.New)
                    effective++;
            }

            if (effective == 0)
                continue;
            var changes = new CellChange[effective];
            var i = 0;
            foreach (var change in log.Changes)
            {
                if (change.Old != change.New)
                    changes[i++] = change;
            }

            layers.Add(new LayerCellChanges(layer, changes));
        }

        return layers.Count == 0 ? null : new TileChangeSet([.. layers]);
    }

    /// <summary>Restores every cell this edit wrote and finishes it.</summary>
    public void Cancel()
    {
        EnsureActive();
        _completed = true;
        foreach (var (layer, log) in _logs)
        {
            var bounds = GridBounds.Empty;
            for (var i = log.Changes.Count - 1; i >= 0; i--)
            {
                var change = log.Changes[i];
                if (layer.Exchange(change.Cell, change.Old) != change.Old)
                    bounds = bounds.Include(change.Cell);
            }

            if (!bounds.IsEmpty && layer.Map == Map)
                Map.RaiseChanged(new MapChange(MapChangeKind.Cells, layer, Cells: bounds));
        }
    }

    private LayerLog Log(TileLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        EnsureActive();
        if (layer.Map != Map)
            throw new InvalidOperationException($"Layer \"{layer.Name}\" is not part of the edited map.");
        if (!_logs.TryGetValue(layer, out var log))
            _logs[layer] = log = new LayerLog();
        return log;
    }

    private TileCell Write(TileLayer layer, LayerLog log, GridCoord cell, TileCell value)
    {
        var previous = layer.Exchange(cell, value);
        if (log.Indices.TryGetValue(cell, out var index))
        {
            log.Changes[index] = log.Changes[index] with { New = value };
        }
        else if (previous != value)
        {
            log.Indices.Add(cell, log.Changes.Count);
            log.Changes.Add(new CellChange(cell, previous, value));
            Count++;
        }

        return previous;
    }

    private void Notify(TileLayer layer, GridBounds bounds)
    {
        if (!bounds.IsEmpty)
            Map.RaiseChanged(new MapChange(MapChangeKind.Cells, layer, Cells: bounds));
    }

    private void EnsureActive()
    {
        if (_completed)
            throw new InvalidOperationException("The edit has already been committed or cancelled.");
    }

    private sealed class LayerLog
    {
        public Dictionary<GridCoord, int> Indices { get; } = new();

        public List<CellChange> Changes { get; } = [];
    }
}
