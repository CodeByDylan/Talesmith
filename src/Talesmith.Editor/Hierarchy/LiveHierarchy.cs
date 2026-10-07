using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;

namespace Talesmith.Editor.Hierarchy;

/// <summary>One entity of the play world as the hierarchy shows it, copied on the game thread.</summary>
/// <param name="Key">The document id the scene gave the entity, or a stand-in for spawned entities.</param>
/// <param name="Icon">The icon name of the entity's primary component, or null.</param>
public sealed record LiveEntityInfo(Entity Entity, Entity Parent, Guid Key, string Name, bool Active, string? Icon, bool IsCamera, bool IsPrefab);

/// <summary>The rows of the play session's world: every entity with a transform, a name or a scene id, spawned ones included, nested by their
/// parents.</summary>
/// <remarks><see cref="Capture"/> runs on the game thread and copies the world into <see cref="LiveEntityInfo"/>s; <see cref="Refresh"/> runs on
/// the UI thread and replaces the rows only when entities, names or nesting changed, reusing the rows of entities that remain, so selection and
/// expansion stay put.</remarks>
public sealed class LiveHierarchy
{
    private static readonly QueryDescription Shown = new QueryDescription().WithAny<Transform>().WithAny<Name>().WithAny<SceneEntityId>();

    private readonly Dictionary<Entity, HierarchyRow> _rows = [];
    private readonly HashSet<Entity> _expanded = [];
    private readonly List<(LiveEntityInfo Info, int Depth)> _order = [];
    private readonly Dictionary<Entity, List<LiveEntityInfo>> _children = [];
    private readonly Dictionary<Entity, Entity> _parents = [];
    private readonly List<LiveEntityInfo> _roots = [];
    private string _filter = "";

    public ObservableCollection<HierarchyRow> Rows { get; private set; } = [];

    /// <summary>Copies the entities of a game's current scene; call on the game thread, through <c>IPlayModeService.InvokeAsync</c>.</summary>
    public static IReadOnlyList<LiveEntityInfo> Capture(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Scenes.Current?.World is not { } world)
            return [];
        var registry = game.Services.GetService<ComponentRegistry>();
        var icons = new Dictionary<int, string?>();
        var result = new List<LiveEntityInfo>();
        foreach (var archetype in world.Query(Shown))
        {
            foreach (var entity in archetype.Entities)
            {
                var name = world.TryGet<Name>(entity, out var named) && !string.IsNullOrEmpty(named.Value) ? named.Value : $"Entity {entity.Id}";
                var parent = world.TryGet<Parent>(entity, out var p) ? p.Value : Entity.Null;
                result.Add(new LiveEntityInfo(entity, parent, KeyOf(world, entity), name, !world.Has<Inactive>(entity), IconOf(world, entity, registry, icons),
                    world.Has<Camera>(entity), world.Has<PrefabInstance>(entity)));
            }
        }

        return result;
    }

    /// <summary>Turns a play world entity's active state around; call on the game thread.</summary>
    public static void ToggleActive(Game game, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Scenes.Current?.World is not { } world || !world.IsAlive(entity))
            return;
        if (world.Has<Inactive>(entity))
            world.Remove<Inactive>(entity);
        else
            world.Set(entity, new Inactive());
    }

    public void Clear()
    {
        _rows.Clear();
        _expanded.Clear();
        _parents.Clear();
        Rows = [];
    }

    public HierarchyRow? Find(Entity entity) => _rows.GetValueOrDefault(entity);

    /// <summary>The parent row of an entity, or null for roots.</summary>
    public HierarchyRow? GetParent(Entity entity) => _parents.TryGetValue(entity, out var parent) ? Find(parent) : null;

    public void SetFilter(string? filter) => _filter = filter?.Trim() ?? "";

    public void SetExpanded(Entity entity, bool expanded)
    {
        if (expanded)
            _expanded.Add(entity);
        else
            _expanded.Remove(entity);
        if (_rows.TryGetValue(entity, out var row))
            row.IsExpanded = expanded;
    }

    /// <summary>Expands the ancestors of an entity, as of the last refresh.</summary>
    public void Reveal(Entity entity)
    {
        var guard = 0;
        for (var current = _parents.GetValueOrDefault(entity); !current.IsNull && guard++ < 256; current = _parents.GetValueOrDefault(current))
            SetExpanded(current, true);
    }

    /// <summary>Updates the rows from a capture; returns whether they were replaced.</summary>
    /// <param name="iconOf">The icon of the scene entity a live entity was created for, so both look the same, or null.</param>
    public bool Refresh(IReadOnlyList<LiveEntityInfo> entities, Func<Guid, global::Avalonia.Media.Geometry?>? iconOf = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        _children.Clear();
        _roots.Clear();
        _parents.Clear();
        var seen = new HashSet<Entity>();
        foreach (var info in entities)
            seen.Add(info.Entity);
        foreach (var info in entities)
        {
            if (!info.Parent.IsNull && seen.Contains(info.Parent))
            {
                _parents[info.Entity] = info.Parent;
                if (!_children.TryGetValue(info.Parent, out var list))
                    _children[info.Parent] = list = [];
                list.Add(info);
            }
            else
            {
                _roots.Add(info);
            }
        }

        _roots.Sort(Compare);
        foreach (var list in _children.Values)
            list.Sort(Compare);

        var changed = false;
        foreach (var stale in _rows.Keys.Where(e => !seen.Contains(e)).ToList())
        {
            _rows.Remove(stale);
            changed = true;
        }

        _order.Clear();
        foreach (var root in _roots)
            Walk(root, 0, filtering: _filter.Length > 0);

        var next = new List<HierarchyRow>(_order.Count);
        foreach (var (info, depth) in _order)
        {
            if (!_rows.TryGetValue(info.Entity, out var row))
            {
                row = new HierarchyRow(info.Key, HierarchyRowKind.Live) { LiveEntity = info.Entity, IsExpanded = _expanded.Contains(info.Entity) };
                _rows[info.Entity] = row;
                changed = true;
            }

            if (row.Name != info.Name)
            {
                row.Name = info.Name;
                changed = true;
            }

            row.Depth = depth;
            row.HasChildren = _children.ContainsKey(info.Entity);
            row.IsActive = info.Active;
            row.IsPrefabInstance = info.IsPrefab;
            row.Icon ??= iconOf?.Invoke(info.Key) ?? (info.IsCamera ? Icons.Camera : Icons.Find(info.Icon) ?? (info.IsPrefab ? Icons.Package : Icons.Box));
            if (_filter.Length > 0)
            {
                var start = row.Name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase);
                row.SetMatch(start, start < 0 ? 0 : _filter.Length);
                row.IsContext = start < 0;
            }
            else
            {
                row.SetMatch(0, 0);
                row.IsContext = false;
            }

            next.Add(row);
        }

        if (!changed && next.Count == Rows.Count && next.SequenceEqual(Rows))
            return false;
        Rows = new ObservableCollection<HierarchyRow>(next);
        return true;
    }

    private bool Walk(LiveEntityInfo info, int depth, bool filtering)
    {
        var index = _order.Count;
        _order.Add((info, depth));
        var children = _children.GetValueOrDefault(info.Entity);
        if (!filtering)
        {
            if (children is not null && _expanded.Contains(info.Entity))
            {
                foreach (var child in children)
                    Walk(child, depth + 1, filtering);
            }

            return true;
        }

        var any = false;
        if (children is not null)
        {
            foreach (var child in children)
                any |= Walk(child, depth + 1, filtering);
        }

        if (any || info.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
            return true;
        _order.RemoveRange(index, _order.Count - index);
        return false;
    }

    private static int Compare(LiveEntityInfo a, LiveEntityInfo b) => a.Entity.Id.CompareTo(b.Entity.Id);

    private static Guid KeyOf(World world, Entity entity)
    {
        if (world.TryGet<SceneEntityId>(entity, out var id))
            return id.Value;
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, entity.Id);
        BitConverter.TryWriteBytes(bytes[4..], entity.Version);
        bytes[15] = 0x7F;
        return new Guid(bytes);
    }

    private static string? IconOf(World world, Entity entity, ComponentRegistry? registry, Dictionary<int, string?> icons)
    {
        if (registry is null)
            return null;
        foreach (var id in world.GetComponentIds(entity))
        {
            if (!icons.TryGetValue(id, out var icon))
            {
                var type = ComponentType.FromId(id).Type;
                icon = registry.TryGet(type, out var definition) && !definition.Info.Hidden && definition.TypeName is not ("Transform" or "Tags")
                    && Icons.Exists(definition.Info.Icon) ? definition.Info.Icon
                    : null;
                icons[id] = icon;
            }

            if (icon is not null)
                return icon;
        }

        return null;
    }
}
