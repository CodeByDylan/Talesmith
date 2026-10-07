using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Prefabs;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Hierarchy;

/// <summary>The scene's entity tree and the flat list of rows it shows, kept up to date change by change.</summary>
/// <remarks>
/// Rows are the expanded part of the tree in order, so a virtualized list can show thousands of entities. Each <see cref="SceneChange"/> touches
/// only the rows of the entities it names: property edits change nothing, renames and flags update one row, and added, removed or moved entities
/// insert or remove their own rows. Prefab instances get rows for the entities of their prefab. While <see cref="Filter"/> is set, the rows are
/// the matching entities with their ancestors.
/// </remarks>
public sealed partial class HierarchyTree(PrefabInstances instances, PrefabLibrary library, EntityIcons icons) : ObservableObject
{
    private readonly Dictionary<Guid, Node> _nodes = [];
    private readonly List<Node> _roots = [];
    private readonly HashSet<Guid> _expanded = [];
    private SceneDocumentModel? _model;

    [ObservableProperty]
    private HierarchyRows _rows = [];

    /// <summary>The text rows must contain, or empty for the whole tree.</summary>
    public string Filter { get; private set; } = "";

    public bool IsFiltering => Filter.Length > 0;

    /// <summary>The number of entities in the tree, prefab members included.</summary>
    public int Count => _nodes.Count;

    public SceneDocumentModel? Model => _model;

    /// <summary>Shows a scene, rebuilding every row.</summary>
    public void Load(SceneDocumentModel? model)
    {
        _model = model;
        Rebuild();
    }

    /// <summary>Rebuilds the tree from the scene, keeping which entities are expanded.</summary>
    public void Rebuild()
    {
        _nodes.Clear();
        _roots.Clear();
        if (_model is { } model)
        {
            foreach (var entity in model.Entities)
            {
                var parent = entity.Parent is { } id ? _nodes.GetValueOrDefault(id) : null;
                var node = CreateNode(entity, parent);
                (parent?.Children ?? _roots).Add(node);
                AddMembers(node);
            }

            foreach (var node in _nodes.Values)
                node.Row.HasChildren = node.Children.Count > 0;
        }

        ResetRows();
    }

    /// <summary>Updates the rows for one change of the scene.</summary>
    public void Apply(SceneChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_model is not { } model)
            return;
        switch (change.Kind)
        {
            case SceneChangeKind.Reloaded:
                Rebuild();
                return;
            case SceneChangeKind.EntityAdded when model.Find(change.Entity) is { } entity && !_nodes.ContainsKey(entity.Id):
                Insert(entity);
                break;
            case SceneChangeKind.EntityRemoved when _nodes.TryGetValue(change.Entity, out var removed):
                Detach(removed);
                Forget(removed);
                break;
            case SceneChangeKind.EntityMoved when _nodes.TryGetValue(change.Entity, out var moved) && model.Find(change.Entity) is { } entity:
                Detach(moved);
                Attach(moved, entity);
                break;
            case SceneChangeKind.EntityRenamed when _nodes.TryGetValue(change.Entity, out var renamed) && model.Find(change.Entity) is { } entity:
                renamed.Row.Name = EntityDataService.DisplayName(entity.Name);
                UpdateMatch(renamed);
                break;
            case SceneChangeKind.EntityStateChanged when _nodes.TryGetValue(change.Entity, out var flagged) && model.Find(change.Entity) is { } entity:
                UpdateFlags(flagged.Row, entity);
                break;
            case SceneChangeKind.ComponentAdded or SceneChangeKind.ComponentRemoved when _nodes.TryGetValue(change.Entity, out var changed) &&
                                                                                       model.Find(change.Entity) is { } entity:
                changed.Row.Icon = IconOf(entity);
                changed.Row.HasOverrides = HasOverrides(entity);
                break;
            case SceneChangeKind.PropertyChanged when model.Find(change.Entity) is { Prefab: not null } entity && _nodes.TryGetValue(entity.Id, out var instance):
                instance.Row.HasOverrides = HasOverrides(entity);
                break;
            case SceneChangeKind.EntityReplaced when _nodes.TryGetValue(change.Entity, out var replaced) && model.Find(change.Entity) is { } entity:
                RefreshInstance(replaced, entity);
                break;
            default:
                return;
        }

        if (IsFiltering && change.Kind is not (SceneChangeKind.EntityStateChanged or SceneChangeKind.PropertyChanged))
            ResetRows();
    }

    /// <summary>Refreshes the prefab entities of instances, such as after their prefab file changed.</summary>
    public void RefreshInstances()
    {
        if (_model is not { } model)
            return;
        foreach (var node in _nodes.Values.Where(n => n.Row.IsPrefabInstance).ToList())
        {
            if (model.Find(node.Id) is { } entity)
                RefreshInstance(node, entity);
        }

        if (IsFiltering)
            ResetRows();
    }

    public HierarchyRow? Find(Guid id) => _nodes.GetValueOrDefault(id)?.Row;

    /// <summary>The parent of an entity in the tree, or null for roots.</summary>
    public HierarchyRow? GetParent(Guid id) => _nodes.GetValueOrDefault(id)?.Parent?.Row;

    /// <summary>The children of an entity in the tree, prefab members first; null gives the roots.</summary>
    public IReadOnlyList<HierarchyRow> GetChildren(Guid? id) =>
        id is { } key ? _nodes.TryGetValue(key, out var node) ? [.. node.Children.Select(c => c.Row)] : [] : [.. _roots.Select(r => r.Row)];

    /// <summary>The entity and every descendant, in tree order.</summary>
    public IReadOnlyList<HierarchyRow> GetSubtree(Guid id)
    {
        var result = new List<HierarchyRow>();
        if (_nodes.TryGetValue(id, out var node))
            Collect(node, result, onlyExpanded: false);
        return result;
    }

    public int IndexOf(Guid id) => _nodes.TryGetValue(id, out var node) ? Rows.IndexOf(node.Row) : -1;

    /// <summary>Expands or collapses an entity, inserting or removing the rows of its descendants.</summary>
    public void SetExpanded(Guid id, bool expanded)
    {
        if (!_nodes.TryGetValue(id, out var node))
            return;
        if (expanded)
            _expanded.Add(id);
        else
            _expanded.Remove(id);
        if (node.Row.IsExpanded == expanded)
            return;
        var index = IsFiltering || !IsShown(node) ? -1 : Rows.IndexOf(node.Row);
        // CountVisible counts the descendants only while the row is still expanded.
        if (!expanded && index >= 0)
            Rows.RemoveRange(index + 1, CountVisible(node) - 1);
        node.Row.IsExpanded = expanded;
        if (expanded && index >= 0)
        {
            var rows = new List<HierarchyRow>();
            foreach (var child in node.Children)
                Collect(child, rows, onlyExpanded: true);
            Rows.InsertRange(index + 1, rows);
        }
    }

    /// <summary>Expands an entity and all its descendants, or collapses them.</summary>
    public void SetExpandedRecursive(Guid id, bool expanded)
    {
        if (!_nodes.TryGetValue(id, out var node))
            return;
        var all = new List<HierarchyRow>();
        Collect(node, all, onlyExpanded: false);
        foreach (var row in expanded ? all : Enumerable.Reverse(all))
        {
            if (row.HasChildren)
                SetExpanded(row.Id, expanded);
        }
    }

    /// <summary>Expands the ancestors of an entity so its row shows.</summary>
    public void Reveal(Guid id)
    {
        if (!_nodes.TryGetValue(id, out var node))
            return;
        var chain = new Stack<Node>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            chain.Push(parent);
        while (chain.TryPop(out var ancestor))
            SetExpanded(ancestor.Id, true);
    }

    /// <summary>Shows only rows whose name contains <paramref name="filter"/>, with their ancestors.</summary>
    public void SetFilter(string? filter)
    {
        filter = filter?.Trim() ?? "";
        if (filter == Filter)
            return;
        Filter = filter;
        OnPropertyChanged(nameof(IsFiltering));
        ResetRows();
    }

    private Node CreateNode(EntityDocument entity, Node? parent)
    {
        var row = new HierarchyRow(entity.Id, HierarchyRowKind.Entity) { Name = EntityDataService.DisplayName(entity.Name) };
        UpdateFlags(row, entity);
        row.Icon = IconOf(entity);
        row.IsPrefabInstance = entity.Prefab is not null;
        row.HasOverrides = HasOverrides(entity);
        row.IsPrefabMissing = entity.Prefab is { } link && library.Get(link.Asset) is null;
        row.IsExpanded = _expanded.Contains(entity.Id);
        var node = new Node(row) { Parent = parent };
        _nodes[entity.Id] = node;
        UpdateMatch(node);
        return node;
    }

    private void AddMembers(Node instance)
    {
        if (!instance.Row.IsPrefabInstance)
            return;
        var position = 0;
        foreach (var member in instances.GetMembers(instance.Id))
        {
            if (member.IsRoot || _nodes.ContainsKey(member.Id))
                continue;
            var parent = member.Entity.Parent is { } id && _nodes.TryGetValue(id, out var found) ? found : instance;
            var row = new HierarchyRow(member.Id, HierarchyRowKind.PrefabMember)
            {
                Name = EntityDataService.DisplayName(member.Entity.Name),
                Icon = icons.Get(member.Entity.Components, !member.Entity.Prefab.IsEmpty),
                IsActive = member.Entity.Active,
                IsPrefabInstance = false,
                HasOverrides = PrefabInstances.HasOverrides(member),
                IsExpanded = _expanded.Contains(member.Id)
            };
            var node = new Node(row) { Parent = parent };
            _nodes[member.Id] = node;
            if (ReferenceEquals(parent, instance))
                parent.Children.Insert(position++, node);
            else
                parent.Children.Add(node);
            UpdateMatch(node);
        }

        foreach (var node in Descendants(instance))
            node.Row.HasChildren = node.Children.Count > 0;
        instance.Row.HasChildren = instance.Children.Count > 0;
    }

    private void RefreshInstance(Node node, EntityDocument entity)
    {
        var shown = !IsFiltering && IsShown(node);
        var index = shown ? Rows.IndexOf(node.Row) : -1;
        var members = node.Children.Where(c => c.Row.Kind == HierarchyRowKind.PrefabMember).ToList();
        if (index >= 0 && node.Row.IsExpanded)
        {
            Rows.RemoveRange(index + 1, members.Sum(CountVisibleIfShown));
        }

        foreach (var member in members)
        {
            node.Children.Remove(member);
            Forget(member);
        }

        UpdateFlags(node.Row, entity);
        node.Row.Name = EntityDataService.DisplayName(entity.Name);
        node.Row.IsPrefabInstance = entity.Prefab is not null;
        node.Row.Icon = IconOf(entity);
        node.Row.HasOverrides = HasOverrides(entity);
        node.Row.IsPrefabMissing = entity.Prefab is { } link && library.Get(link.Asset) is null;
        AddMembers(node);
        node.Row.HasChildren = node.Children.Count > 0;
        if (index >= 0 && node.Row.IsExpanded)
        {
            var rows = new List<HierarchyRow>();
            foreach (var child in node.Children.Where(c => c.Row.Kind == HierarchyRowKind.PrefabMember))
                Collect(child, rows, onlyExpanded: true);
            Rows.InsertRange(index + 1, rows);
        }
    }

    private int CountVisibleIfShown(Node node) => CountVisible(node);

    private void Insert(EntityDocument entity)
    {
        var parent = entity.Parent is { } id ? _nodes.GetValueOrDefault(id) : null;
        var node = CreateNode(entity, parent);
        AddMembers(node);
        Attach(node, entity);
    }

    /// <summary>Puts a detached node under the entity's parent at its place among the siblings and inserts its rows.</summary>
    private void Attach(Node node, EntityDocument entity)
    {
        var parent = entity.Parent is { } id ? _nodes.GetValueOrDefault(id) : null;
        node.Parent = parent;
        var siblings = parent?.Children ?? _roots;
        var members = siblings.Count(c => c.Row.Kind == HierarchyRowKind.PrefabMember);
        var index = Math.Clamp(members + _model!.GetSiblingIndex(entity.Id), members, siblings.Count);
        siblings.Insert(index, node);
        if (parent is not null)
            parent.Row.HasChildren = true;
        node.Row.Depth = parent is null ? 0 : parent.Row.Depth + 1;
        foreach (var descendant in Descendants(node))
            descendant.Row.Depth = descendant.Parent!.Row.Depth + 1;
        if (IsFiltering || !IsShown(node))
            return;

        var rowIndex = RowIndexFor(node);
        var rows = new List<HierarchyRow>();
        Collect(node, rows, onlyExpanded: true);
        Rows.InsertRange(rowIndex, rows);
    }

    /// <summary>Removes a node's rows and takes it out of its parent, keeping its descendants attached to it.</summary>
    private void Detach(Node node)
    {
        if (!IsFiltering)
        {
            var index = Rows.IndexOf(node.Row);
            if (index >= 0)
            {
                Rows.RemoveRange(index, Math.Min(CountVisible(node), Rows.Count - index));
            }
        }

        var siblings = node.Parent?.Children ?? _roots;
        siblings.Remove(node);
        if (node.Parent is { } parent)
            parent.Row.HasChildren = parent.Children.Count > 0;
        node.Parent = null;
    }

    private void Forget(Node node)
    {
        _nodes.Remove(node.Id);
        foreach (var child in node.Children)
            Forget(child);
    }

    private static bool IsShown(Node node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (!parent.Row.IsExpanded)
                return false;
        }

        return true;
    }

    /// <summary>The row index where a shown node's rows go: after its previous sibling's rows, or after its parent's row.</summary>
    private int RowIndexFor(Node node)
    {
        var siblings = node.Parent?.Children ?? _roots;
        var position = siblings.IndexOf(node);
        if (position > 0)
        {
            var previous = siblings[position - 1];
            return Rows.IndexOf(previous.Row) + CountVisible(previous);
        }

        return node.Parent is { } parent ? Rows.IndexOf(parent.Row) + 1 : 0;
    }

    /// <summary>The node's row and the rows of its expanded descendants.</summary>
    private static int CountVisible(Node node)
    {
        var count = 1;
        if (!node.Row.IsExpanded)
            return count;
        foreach (var child in node.Children)
            count += CountVisible(child);
        return count;
    }

    private static void Collect(Node node, List<HierarchyRow> rows, bool onlyExpanded)
    {
        rows.Add(node.Row);
        if (onlyExpanded && !node.Row.IsExpanded)
            return;
        foreach (var child in node.Children)
            Collect(child, rows, onlyExpanded);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private void ResetRows()
    {
        var rows = new List<HierarchyRow>();
        if (IsFiltering)
        {
            foreach (var root in _roots)
                CollectFiltered(root, 0, rows);
        }
        else
        {
            foreach (var root in _roots)
            {
                SetDepths(root, 0);
                Collect(root, rows, onlyExpanded: true);
            }
        }

        foreach (var node in _nodes.Values)
            UpdateMatch(node);
        Rows = new HierarchyRows(rows);
    }

    private static void SetDepths(Node node, int depth)
    {
        node.Row.Depth = depth;
        foreach (var child in node.Children)
            SetDepths(child, depth + 1);
    }

    /// <summary>Adds the node when it or a descendant matches; returns whether it did.</summary>
    private bool CollectFiltered(Node node, int depth, List<HierarchyRow> rows)
    {
        node.Row.Depth = depth;
        var index = rows.Count;
        rows.Add(node.Row);
        var matches = Matches(node.Row.Name);
        var any = false;
        foreach (var child in node.Children)
            any |= CollectFiltered(child, depth + 1, rows);
        if (matches || any)
        {
            node.Row.IsContext = !matches;
            return true;
        }

        rows.RemoveAt(index);
        return false;
    }

    private bool Matches(string name) => name.Contains(Filter, StringComparison.OrdinalIgnoreCase);

    private void UpdateMatch(Node node)
    {
        if (!IsFiltering)
        {
            node.Row.SetMatch(0, 0);
            node.Row.IsContext = false;
            return;
        }

        var start = node.Row.Name.IndexOf(Filter, StringComparison.OrdinalIgnoreCase);
        node.Row.SetMatch(start, start < 0 ? 0 : Filter.Length);
    }

    private static void UpdateFlags(HierarchyRow row, EntityDocument entity)
    {
        row.IsActive = entity.Active;
        row.IsHidden = entity.Editor.Hidden;
        row.IsLocked = entity.Editor.Locked;
    }

    private global::Avalonia.Media.Geometry IconOf(EntityDocument entity)
    {
        if (entity.Prefab is not null && instances.Find(entity.Id) is { } member)
            return icons.Get(member.Entity.Components, isPrefabInstance: true);
        return icons.Get(entity.Components, entity.Prefab is not null);
    }

    private bool HasOverrides(EntityDocument entity) => entity.Prefab is not null && instances.Find(entity.Id) is { } member && PrefabInstances.HasOverrides(member);

    private sealed class Node(HierarchyRow row)
    {
        public HierarchyRow Row { get; } = row;

        public Guid Id => Row.Id;

        public Node? Parent { get; set; }

        public List<Node> Children { get; } = [];
    }
}
