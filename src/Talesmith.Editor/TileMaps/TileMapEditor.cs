using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Ecs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Grids;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Maps;
using Talesmith.UI.Controls;
using GridSelectionMode = Talesmith.Grids.SelectionMode;

namespace Talesmith.Editor.TileMaps;

/// <summary>A map shown in the open scene: the entity with its <c>TileMapRenderer</c> and the loaded map, which edits change directly.</summary>
public sealed record MapTarget(Guid EntityId, string Name, TileMap Map);

/// <summary>An object of an object layer, by layer and id.</summary>
public sealed record MapObjectRef(ObjectLayer Layer, int Id)
{
    public MapObject? Object => Layer.Find(Id);
}

/// <summary>The tile map editing session: which map of the scene is edited, its active layer, the brush, the cell selection and the selected
/// object, and the undoable edits every tile tool and panel makes.</summary>
/// <remarks>
/// <para>The target is the map of the selected entity, or one picked in the Tile Map panel, and stays while other entities are selected.
/// Edits change the loaded <see cref="TileMap"/> of the edit world, so the viewport redraws only the chunks that changed.</para>
/// <para>Each edit is one undo step for the map and the open scene together, so the scene shows as unsaved and saving it saves the map.</para>
/// </remarks>
public sealed partial class TileMapEditor : ObservableObject, IDisposable
{
    private readonly IEditWorld _world;
    private readonly ISceneDocumentService _documents;
    private readonly ISelectionService _selection;
    private readonly IUndoService _undo;
    private readonly IToastService _toasts;
    private readonly ViewportService _viewport;
    private readonly Dictionary<TileMap, MapLayer> _activeLayers = new(ReferenceEqualityComparer.Instance);
    private TileMap? _attached;
    private bool _respawnQueued;
    private Guid? _dismissed;
    private bool _followPending;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Map))]
    private MapTarget? _target;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveTileLayer), nameof(ActiveObjectLayer))]
    private MapLayer? _activeLayer;

    [ObservableProperty]
    private GridCoord? _hoveredCell;

    [ObservableProperty]
    private MapObjectRef? _selectedObject;

    [ObservableProperty]
    private bool _isBusy;

    public TileMapEditor(IEditWorld world, ISceneDocumentService documents, ISelectionService selection, IUndoService undo, TileMapDocuments maps,
        IToastService toasts, ViewportService viewport)
    {
        _world = world;
        _documents = documents;
        _selection = selection;
        _undo = undo;
        _toasts = toasts;
        _viewport = viewport;
        Maps = maps;
        selection.Changed += (_, _) =>
        {
            _dismissed = null;
            _followPending = !FollowSelection();
        };
        documents.ActiveChanged += (_, _) => Target = null;
        world.Changed += (_, _) =>
        {
            Resolve();
            if (Target is null || _followPending)
                _followPending = !FollowSelection();
        };
        _followPending = !FollowSelection();
    }

    /// <summary>The map being edited, or null.</summary>
    public TileMap? Map => Target?.Map;

    public TileMapDocuments Maps { get; }

    public TileLayer? ActiveTileLayer => ActiveLayer as TileLayer;

    public ObjectLayer? ActiveObjectLayer => ActiveLayer as ObjectLayer;

    public TileBrushSettings Brush { get; } = new();

    /// <summary>The selected cells of the active tile layer; change it through <see cref="Select"/> or <see cref="SetSelection"/>.</summary>
    public CellSet Selection { get; private set; } = new();

    /// <summary>Increases whenever <see cref="Selection"/> changes.</summary>
    public int SelectionVersion { get; private set; }

    /// <summary>The world position of the map's cell (0, 0): its entity's position.</summary>
    public Vector2 Origin => Target is { } target && _world.World is { } runtime && _world.TryGetEntity(target.EntityId, out var entity)
                             && runtime.TryGet<Transform>(entity, out var transform)
        ? transform.Position
        : Vector2.Zero;

    /// <summary>The steps of a full turn on the map's grid: 6 on hex grids, 4 on square grids.</summary>
    public int RotationSteps => Map?.Layout.RotationSteps ?? 6;

    /// <summary>Raised on the UI thread after the edited map changed, with what changed.</summary>
    public event EventHandler<MapChange>? MapChanged;

    /// <summary>Raised after <see cref="Selection"/> changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The maps shown by entities of the open scene, in scene order.</summary>
    public IReadOnlyList<MapTarget> FindTargets()
    {
        if (_documents.Active is not { } scene)
            return [];
        var targets = new List<MapTarget>();
        foreach (var entity in scene.Entities)
        {
            if (entity.FindComponent("TileMapRenderer") is not null && Resolve(entity.Id) is { } target)
                targets.Add(target);
        }

        return targets;
    }

    /// <summary>Edits the map of a scene entity; returns false when the entity shows no loaded map.</summary>
    public bool Edit(Guid entityId)
    {
        if (Resolve(entityId) is not { } target)
            return false;
        _dismissed = null;
        _followPending = false;
        Target = target;
        return true;
    }

    /// <summary>Stops editing until another map is picked or selected.</summary>
    public void Close()
    {
        _dismissed = Target?.EntityId;
        Target = null;
    }

    public GridCoord CellAt(Vector2 world) => Map is { } map ? map.Layout.WorldToCell(world - Origin) : default;

    /// <summary>The world position of a cell's center.</summary>
    public Vector2 CellCenter(GridCoord cell) => Map is { } map ? Origin + map.Layout.CellToWorld(cell) : Vector2.Zero;

    /// <summary>Gets the active tile layer when it can be painted, otherwise tells the user why not.</summary>
    public bool TryGetEditableTileLayer([NotNullWhen(true)] out TileLayer? layer)
    {
        layer = ActiveTileLayer;
        if (layer is null)
        {
            Warn("Select a tile layer", "This tool paints tiles. Pick a tile layer in the Tile Map panel.");
            return false;
        }

        if (CanEdit(layer))
            return true;
        layer = null;
        return false;
    }

    /// <summary>Gets the active object layer when it can be edited, otherwise tells the user why not.</summary>
    public bool TryGetEditableObjectLayer([NotNullWhen(true)] out ObjectLayer? layer)
    {
        layer = ActiveObjectLayer;
        if (layer is null)
        {
            Warn("Select an object layer", "Objects are placed on object layers. Add one in the Tile Map panel.");
            return false;
        }

        if (CanEdit(layer))
            return true;
        layer = null;
        return false;
    }

    /// <summary>Whether a layer can be edited, telling the user when it is locked or hidden.</summary>
    public bool CanEdit(MapLayer layer)
    {
        if (layer.IsLocked)
        {
            Warn("Layer is locked", $"Unlock \"{layer.Name}\" to edit it.");
            return false;
        }

        if (!layer.IsVisible)
        {
            Warn("Layer is hidden", $"Show \"{layer.Name}\" to edit it.");
            return false;
        }

        return true;
    }

    /// <summary>Starts changing cells of the edited map; finish with <see cref="Commit"/> or cancel the returned edit.</summary>
    public TileEdit BeginEdit() => new(Map ?? throw new InvalidOperationException("No tile map is being edited."));

    /// <summary>Finishes a cell edit as one undo step; returns false when no cell changed.</summary>
    /// <param name="description">The step's description; <c>{0}</c> is replaced by the number of changed tiles, such as "Paint {0}".</param>
    public bool Commit(TileEdit edit, string description)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Commit() is not { } changes)
            return false;
        Record(string.Format(System.Globalization.CultureInfo.CurrentCulture, description, Tiles(changes.Count)), changes.Invert(), edit.Map);
        return true;
    }

    /// <summary>Applies a map edit as one undo step.</summary>
    public void Execute(string description, IMapEdit edit, TileMap? map = null)
    {
        map ??= Map ?? throw new InvalidOperationException("No tile map is being edited.");
        _undo.Execute(Step(MapEditCommand.Pending(description, map, edit), map));
    }

    /// <summary>Records an edit that was already applied as one undo step, given the edit that reverts it.</summary>
    public void Record(string description, IMapEdit inverse, TileMap? map = null)
    {
        map ??= Map ?? throw new InvalidOperationException("No tile map is being edited.");
        _undo.Record(Step(MapEditCommand.Applied(description, map, inverse), map));
    }

    /// <summary>Groups the edits until the result is disposed into one undo step, such as while dragging a slider.</summary>
    public UndoTransaction BeginTransaction(string description) => _undo.BeginTransaction(description);

    /// <summary>Combines cells with the selection.</summary>
    public void Select(CellSet cells, GridSelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(cells);
        Selection.Apply(cells, mode);
        RaiseSelectionChanged();
    }

    /// <summary>Replaces the selection, such as after moving the selected tiles.</summary>
    public void SetSelection(CellSet cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        Selection = cells;
        RaiseSelectionChanged();
    }

    public void ClearSelection()
    {
        if (Selection.IsEmpty)
            return;
        Selection = new CellSet();
        RaiseSelectionChanged();
    }

    /// <summary>Copies the selected tiles of the active layer into the stamp; returns false when there is nothing to copy.</summary>
    public bool Copy()
    {
        if (Selection.IsEmpty || ActiveTileLayer is not { } layer || Map is not { } map)
            return false;
        var stamp = TileClipboard.Copy(layer, Selection, map.Layout.Topology);
        if (stamp.IsEmpty)
        {
            _toasts.Show("Nothing to copy", "The selection holds no tiles on the active layer.");
            return false;
        }

        Brush.Stamp = stamp;
        _toasts.Show($"Copied {Tiles(stamp.Count)}", "Paste with Ctrl+V.", ToastKind.Success);
        return true;
    }

    /// <summary>Copies the selected tiles, then clears them.</summary>
    public bool Cut()
    {
        if (Selection.IsEmpty || !TryGetEditableTileLayer(out var layer) || !Copy())
            return false;
        var edit = BeginEdit();
        TileClipboard.Delete(edit, layer, Selection);
        return Commit(edit, "Cut {0}");
    }

    /// <summary>Clears the selected tiles.</summary>
    public bool DeleteSelection()
    {
        if (Selection.IsEmpty || !TryGetEditableTileLayer(out var layer))
            return false;
        var edit = BeginEdit();
        TileClipboard.Delete(edit, layer, Selection);
        return Commit(edit, "Delete {0}");
    }

    /// <summary>Paints the brush into every selected cell.</summary>
    public bool FillSelection()
    {
        if (Selection.IsEmpty || Brush.Tiles.Count == 0 || !TryGetEditableTileLayer(out var layer))
            return false;
        var edit = BeginEdit();
        edit.Paint(layer, Selection, Brush.CreateBrush(RotationSteps));
        return Commit(edit, "Fill {0}");
    }

    /// <summary>Selects every placed tile of the active layer.</summary>
    public void SelectAll()
    {
        if (ActiveTileLayer is not { } layer)
            return;
        var cells = new CellSet();
        foreach (var placed in layer.Cells)
            cells.Add(placed.Cell);
        SetSelection(cells);
    }

    /// <summary>Deletes the selected object.</summary>
    public bool DeleteSelectedObject()
    {
        if (SelectedObject is not { Object: { } mapObject } selected || !CanEdit(selected.Layer))
            return false;
        Execute($"Delete \"{mapObject.Name}\"", MapEdits.RemoveObject(selected.Layer, mapObject.Id));
        SelectedObject = null;
        return true;
    }

    /// <summary>Shows a warning toast, such as why a tool cannot paint.</summary>
    public void Warn(string title, string message) => _toasts.Show(title, message, ToastKind.Warning);

    public void Dispose()
    {
        _disposed = true;
        Attach(null);
    }

    /// <summary>Formats a tile count, such as "1 tile" or "12 tiles".</summary>
    public static string Tiles(int count) => count == 1 ? "1 tile" : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{count:N0} tiles");

    partial void OnTargetChanged(MapTarget? oldValue, MapTarget? newValue)
    {
        if (ReferenceEquals(oldValue?.Map, newValue?.Map))
            return;
        Attach(newValue?.Map);
        Selection = new CellSet();
        SelectionVersion++;
        SelectedObject = null;
        HoveredCell = null;
        if (newValue?.Map is not { } map)
        {
            ActiveLayer = null;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Maps.Track(map);
        ActiveLayer = _activeLayers.TryGetValue(map, out var remembered) && map.IndexOf(remembered) >= 0
            ? remembered
            : map.TileLayers.LastOrDefault() ?? map.Layers.LastOrDefault();
        if (Brush.Tiles.Count == 0 || map.FindTileset(Brush.Tiles[0].Tile.TilesetId) is null)
            Brush.Tiles = map.Tilesets.FirstOrDefault(t => t.TileCount > 0) is { } tileset ? [new TileChoice(new TileCell(tileset.Id, 0))] : [];
        if (Brush.Terrain is { } terrain && map.FindTerrain(terrain.Id) != terrain)
            Brush.Terrain = null;
        Brush.Stamp = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveLayerChanged(MapLayer? value)
    {
        if (Map is { } map && value is not null)
            _activeLayers[map] = value;
        if (value is not TileLayer)
            ClearSelection();
    }

    private void Attach(TileMap? map)
    {
        if (_attached is not null)
            _attached.Changed -= OnMapChanged;
        _attached = map;
        if (map is not null)
            map.Changed += OnMapChanged;
    }

    private void OnMapChanged(object? sender, MapChange change)
    {
        if (Map is not { } map)
            return;
        switch (change.Kind)
        {
            case MapChangeKind.LayerRemoved when ReferenceEquals(change.Layer, ActiveLayer):
                ActiveLayer = map.Layers.Count == 0 ? null : map.Layers[Math.Clamp(change.Index, 0, map.Layers.Count - 1)];
                break;
            case MapChangeKind.Objects or MapChangeKind.LayerAdded or MapChangeKind.LayerRemoved
                or MapChangeKind.LayerChanged when change.Layer is ObjectLayer:
                QueueRespawn();
                if (SelectedObject is { } selected && (selected.Object is null || map.IndexOf(selected.Layer) < 0))
                    SelectedObject = null;
                break;
            case MapChangeKind.Terrains when Brush.Terrain is { } terrain && map.FindTerrain(terrain.Id) is var current && !ReferenceEquals(current, terrain):
                Brush.Terrain = current;
                break;
        }

        _viewport.Wake();
        MapChanged?.Invoke(this, change);
    }

    /// <summary>Spawns the map's objects again in the viewport, which shows them as entities, once per batch of object edits.</summary>
    private void QueueRespawn()
    {
        if (_respawnQueued)
            return;
        _respawnQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _respawnQueued = false;
            if (_disposed || Map is not { } map || _world.World is not { } runtime || _world.Game?.Services.GetService<MapSpawner>() is not { } spawner)
                return;
            var entities = new List<(Entity Entity, int Layer)>();
            foreach (var archetype in runtime.Query<TileMapComponent>())
            {
                var components = archetype.GetSpan<TileMapComponent>();
                for (var i = 0; i < components.Length; i++)
                {
                    if (ReferenceEquals(components[i].Map, map))
                        entities.Add((archetype.Entities[i], components[i].RenderLayer));
                }
            }

            foreach (var (entity, layer) in entities)
                spawner.Attach(runtime, entity, map, layer);
            _viewport.Wake();
        });
    }

    /// <summary>Edits the map of the most recently selected entity that shows one; returns false when none is loaded yet.</summary>
    private bool FollowSelection()
    {
        for (var i = _selection.Entities.Count - 1; i >= 0; i--)
        {
            if (_selection.Entities[i] != _dismissed && Resolve(_selection.Entities[i]) is { } target)
            {
                Target = target;
                return true;
            }
        }

        return false;
    }

    /// <summary>Follows the target's entity after the world changed, such as when the scene was loaded again.</summary>
    private void Resolve()
    {
        if (Target is not { } target || _world.IsBusy)
            return;
        var current = Resolve(target.EntityId);
        if (current is null && _documents.Active?.Contains(target.EntityId) == true)
            return;
        if (current != target)
            Target = current;
    }

    private MapTarget? Resolve(Guid entityId)
    {
        if (_world.World is not { } runtime || !_world.TryGetEntity(entityId, out var entity) || !runtime.TryGet<TileMapComponent>(entity, out var shown))
            return null;
        var name = _documents.Active?.Find(entityId)?.Name;
        return new MapTarget(entityId, string.IsNullOrEmpty(name) ? Talesmith.Assets.AssetPath.GetFileName(shown.Map.Path) : name, shown.Map);
    }

    private IUndoableCommand Step(MapEditCommand command, TileMap map) =>
        _documents.Active is { } scene && FindTargets().Any(t => ReferenceEquals(t.Map, map))
            ? new CompositeCommand(command.Description, [command, new SceneTouch(scene)])
            : command;

    private void RaiseSelectionChanged()
    {
        SelectionVersion++;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
