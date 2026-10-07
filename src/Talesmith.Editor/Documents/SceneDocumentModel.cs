using System.Text.Json.Nodes;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Documents;

/// <summary>An open scene: the <see cref="SceneDocument"/> with fine-grained change events and undoable edit operations.</summary>
/// <remarks>
/// <para>Read the document freely but change it only through this class: every edit method here records an undo step that stores what it
/// changed (a property path with its old and new JSON value, a moved subtree, a removed component), applies it and raises
/// <see cref="Changed"/>. Edits of the same property in quick succession merge into one step, and a transaction groups several edits.</para>
/// <para>Entities are kept in hierarchy order: every entity is followed by its descendants. Sibling indexes count only the entity's siblings.
/// Use it on the UI thread.</para>
/// </remarks>
public sealed class SceneDocumentModel
{
    private readonly IUndoService _undo;
    private readonly Dictionary<Guid, EntityDocument> _byId = [];

    public SceneDocumentModel(SceneDocument document, string? path, IUndoService undo)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        Path = path;
        _undo = undo;
        if (Document.Id == Guid.Empty)
            Document.Id = Guid.NewGuid();
        Reindex();
    }

    /// <summary>The document; do not change it directly.</summary>
    public SceneDocument Document { get; private set; }

    /// <summary>The scene's asset path, such as <c>scenes/main.tscene</c>, or null for a scene that was never saved.</summary>
    public string? Path { get; internal set; }

    public string Title => Path is null ? "Untitled" : System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The file name shown in the title bar, such as <c>main.tscene</c>.</summary>
    public string FileName => Path is null ? "Untitled.tscene" : System.IO.Path.GetFileName(Path);

    public bool IsDirty => _undo.IsDirty(this);

    public IReadOnlyList<EntityDocument> Entities => Document.Entities;

    public event EventHandler<SceneChangedEventArgs>? Changed;

    public EntityDocument? Find(Guid id) => _byId.GetValueOrDefault(id);

    /// <exception cref="KeyNotFoundException">No entity has the id.</exception>
    public EntityDocument Get(Guid id) => Find(id) ?? throw new KeyNotFoundException($"The scene has no entity {id:N}.");

    public bool Contains(Guid id) => _byId.ContainsKey(id);

    /// <summary>The children of an entity, or the root entities for null, in order.</summary>
    public IReadOnlyList<EntityDocument> GetChildren(Guid? parent) => [.. Document.Entities.Where(e => e.Parent == parent)];

    /// <summary>The entity's position among its siblings.</summary>
    public int GetSiblingIndex(Guid id)
    {
        var entity = Get(id);
        var index = 0;
        foreach (var other in Document.Entities)
        {
            if (other.Id == id)
                return index;
            if (other.Parent == entity.Parent)
                index++;
        }

        return -1;
    }

    /// <summary>Whether <paramref name="id"/> is <paramref name="ancestor"/> or one of its descendants.</summary>
    public bool IsSelfOrDescendant(Guid id, Guid ancestor)
    {
        for (var current = Find(id); current is not null; current = current.Parent is { } parent ? Find(parent) : null)
        {
            if (current.Id == ancestor)
                return true;
        }

        return false;
    }

    /// <summary>The entity followed by all its descendants, in document order.</summary>
    public IReadOnlyList<EntityDocument> GetSubtree(Guid id)
    {
        var start = Document.Entities.FindIndex(e => e.Id == id);
        if (start < 0)
            return [];
        return Document.Entities.GetRange(start, SubtreeLength(start));
    }

    /// <summary>The ids that are not descendants of other ids in the set, in document order.</summary>
    public IReadOnlyList<Guid> GetTopLevel(IEnumerable<Guid> ids)
    {
        var set = ids.Where(Contains).ToHashSet();
        return [.. Document.Entities.Where(e => set.Contains(e.Id) && !HasAncestorIn(e, set)).Select(e => e.Id)];
    }

    /// <summary>Creates an entity with a Transform, or with the given components; returns it.</summary>
    /// <param name="siblingIndex">The position among the parent's children; -1 appends.</param>
    public EntityDocument CreateEntity(string name, Guid? parent = null, IEnumerable<ComponentDocument>? components = null, int siblingIndex = -1)
    {
        var entity = new EntityDocument { Id = Guid.NewGuid(), Name = name, Parent = parent };
        if (components is null)
            entity.Components.Add(new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }));
        else
            entity.Components.AddRange(components.Select(c => c.Clone()));
        InsertEntities([entity], parent, siblingIndex, $"Create {DisplayName(entity)}");
        return Get(entity.Id);
    }

    /// <summary>Inserts entities with their descendants under a parent, such as pasted or instantiated ones; returns the new root ids.</summary>
    /// <param name="entities">Entities in hierarchy order; those whose parent is not in the list become children of <paramref name="parent"/>.</param>
    public IReadOnlyList<Guid> InsertEntities(IReadOnlyList<EntityDocument> entities, Guid? parent, int siblingIndex = -1, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        if (parent is { } p && !Contains(p))
            throw new KeyNotFoundException($"The scene has no entity {p:N}.");
        var ids = entities.Select(e => e.Id).ToHashSet();
        var blocks = new List<SubtreeBlock>();
        var index = siblingIndex;
        foreach (var entity in entities)
        {
            if (entity.Parent is { } own && ids.Contains(own))
            {
                blocks[^1].Entities.Add(entity.Clone());
                continue;
            }

            var copy = entity.Clone();
            copy.Parent = parent;
            blocks.Add(new SubtreeBlock(parent, index < 0 ? -1 : index++, [copy]));
        }

        if (blocks.Count == 0)
            return [];
        _undo.Execute(new StructureCommand(this, description ?? (blocks.Count == 1 ? $"Add {DisplayName(blocks[0].Entities[0])}" : $"Add {blocks.Count} entities"), blocks, insert: true));
        return [.. blocks.Select(b => b.Entities[0].Id)];
    }

    /// <summary>Deletes entities with their descendants.</summary>
    public void DeleteEntities(IEnumerable<Guid> ids)
    {
        var roots = GetTopLevel(ids);
        if (roots.Count == 0)
            return;
        var blocks = roots.Select(id => new SubtreeBlock(Get(id).Parent, GetSiblingIndex(id), [.. GetSubtree(id).Select(e => e.Clone())])).ToList();
        _undo.Execute(new StructureCommand(this, roots.Count == 1 ? $"Delete {DisplayName(Get(roots[0]))}" : $"Delete {roots.Count} entities", blocks, insert: false));
    }

    /// <summary>Copies entities with their descendants, placing each copy after its original; returns the copies' ids.</summary>
    public IReadOnlyList<Guid> DuplicateEntities(IEnumerable<Guid> ids)
    {
        var roots = GetTopLevel(ids);
        if (roots.Count == 0)
            return [];
        var blocks = new List<SubtreeBlock>();
        var offsets = new Dictionary<Guid, int>();
        foreach (var id in roots)
        {
            var source = Get(id);
            var copies = CloneWithNewIds(GetSubtree(id));
            copies[0].Name = NextName(source.Name, source.Parent);
            var shift = offsets.GetValueOrDefault(source.Parent ?? Guid.Empty);
            offsets[source.Parent ?? Guid.Empty] = shift + 1;
            blocks.Add(new SubtreeBlock(source.Parent, GetSiblingIndex(id) + 1 + shift, copies));
        }

        _undo.Execute(new StructureCommand(this, roots.Count == 1 ? $"Duplicate {DisplayName(Get(roots[0]))}" : $"Duplicate {roots.Count} entities", blocks, insert: true));
        return [.. blocks.Select(b => b.Entities[0].Id)];
    }

    /// <summary>Moves an entity with its descendants under another parent, or among its siblings.</summary>
    /// <param name="siblingIndex">The position among the new parent's children after the move; -1 appends.</param>
    /// <exception cref="InvalidOperationException">The new parent is the entity or one of its descendants.</exception>
    public void MoveEntity(Guid id, Guid? newParent, int siblingIndex = -1)
    {
        var entity = Get(id);
        if (newParent is { } target && IsSelfOrDescendant(target, id))
            throw new InvalidOperationException("An entity cannot become a child of itself or its descendants.");
        var oldIndex = GetSiblingIndex(id);
        var count = Document.Entities.Count(e => e.Parent == newParent && e.Id != id);
        var newIndex = siblingIndex < 0 || siblingIndex > count ? count : siblingIndex;
        if (entity.Parent == newParent && oldIndex == newIndex)
            return;
        var description = entity.Parent == newParent ? $"Reorder {DisplayName(entity)}" : $"Reparent {DisplayName(entity)}";
        _undo.Execute(new MoveCommand(this, description, id, entity.Parent, oldIndex, newParent, newIndex));
    }

    public void Rename(Guid id, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var entity = Get(id);
        if (entity.Name == name)
            return;
        _undo.Execute(new EntityFieldCommand(this, $"Rename {DisplayName(entity)} to {name}", id, EntityField.Name, entity.Name, name));
    }

    public void SetActive(Guid id, bool active) => SetFlag(id, EntityField.Active, active, active ? "Activate" : "Deactivate");

    /// <summary>Hides the entity in the editor's viewport only.</summary>
    public void SetHidden(Guid id, bool hidden) => SetFlag(id, EntityField.Hidden, hidden, hidden ? "Hide" : "Show");

    /// <summary>Stops the entity from being picked in the editor's viewport.</summary>
    public void SetLocked(Guid id, bool locked) => SetFlag(id, EntityField.Locked, locked, locked ? "Lock" : "Unlock");

    /// <summary>Adds a component, or replaces the entity's component of the same type.</summary>
    /// <param name="index">The position in the inspector; -1 appends.</param>
    public void AddComponent(Guid id, ComponentDocument component, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(component);
        var entity = Get(id);
        if (entity.FindComponent(component.Type) is { } existing)
        {
            SetProperty(id, component.Type, "", component.Data.DeepClone());
            return;
        }

        index = index < 0 || index > entity.Components.Count ? entity.Components.Count : index;
        _undo.Execute(new ComponentCommand(this, $"Add {ShortType(component.Type)} to {DisplayName(entity)}", id, component.Clone(), index, add: true));
    }

    public void RemoveComponent(Guid id, string type)
    {
        var entity = Get(id);
        var index = entity.Components.FindIndex(c => c.Type == type);
        if (index < 0)
            return;
        _undo.Execute(new ComponentCommand(this, $"Remove {ShortType(type)} from {DisplayName(entity)}", id, entity.Components[index].Clone(), index, add: false));
    }

    /// <summary>Sets a value inside a component's data; consecutive changes of the same property merge into one undo step.</summary>
    /// <param name="path">A path as in <see cref="JsonPaths"/>, such as "position" or "frames.2.duration"; empty replaces the whole data with
    /// <paramref name="value"/>, which must then be an object.</param>
    /// <exception cref="KeyNotFoundException">The entity does not exist or has no such component.</exception>
    public void SetProperty(Guid id, string component, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        var entity = Get(id);
        var data = (entity.FindComponent(component) ?? throw new KeyNotFoundException($"{DisplayName(entity)} has no {component} component.")).Data;
        var old = path.Length == 0 ? data : JsonPaths.Get(data, path);
        if (JsonNode.DeepEquals(old, value))
            return;
        if (path.Length == 0 && value is not JsonObject)
            throw new ArgumentException("The whole data of a component must be an object.", nameof(value));
        var label = path.Length == 0 ? ShortType(component) : $"{ShortType(component)}.{path}";
        _undo.Execute(new PropertyCommand(this, $"Change {label} of {DisplayName(entity)}", id, component, path, old?.DeepClone(), value?.DeepClone()));
    }

    /// <summary>Gets a value inside a component's data, or null when the component or the value is missing.</summary>
    public JsonNode? GetProperty(Guid id, string component, string path)
    {
        var data = Find(id)?.FindComponent(component)?.Data;
        return data is null ? null : path.Length == 0 ? data : JsonPaths.Get(data, path);
    }

    /// <summary>Replaces an entity's document as a whole, keeping its id, parent and place; for edits no other method covers.</summary>
    public void ReplaceEntity(EntityDocument entity, string description)
    {
        ArgumentNullException.ThrowIfNull(entity);
        var current = Get(entity.Id);
        var replacement = entity.Clone();
        replacement.Parent = current.Parent;
        _undo.Execute(new ReplaceEntityCommand(this, description, current.Clone(), replacement));
    }

    public void SetEnvironment(SceneEnvironment environment, string description = "Change scene environment")
    {
        ArgumentNullException.ThrowIfNull(environment);
        _undo.Execute(new EnvironmentCommand(this, description, Document.Environment.Clone(), environment.Clone()));
    }

    /// <summary>Replaces the whole document without recording an undo step, such as when reverting to the saved file.</summary>
    internal void Reload(SceneDocument document)
    {
        Document = document;
        Reindex();
        Raise(new SceneChange(SceneChangeKind.Reloaded, Guid.Empty));
    }

    internal static string DisplayName(EntityDocument entity) => string.IsNullOrWhiteSpace(entity.Name) ? "entity" : entity.Name;

    private static string ShortType(string type) => type[(type.LastIndexOf('.') + 1)..];

    private static List<EntityDocument> CloneWithNewIds(IReadOnlyList<EntityDocument> subtree)
    {
        var map = subtree.ToDictionary(e => e.Id, _ => Guid.NewGuid());
        return [.. subtree.Select(e =>
        {
            var copy = e.Clone();
            copy.Id = map[e.Id];
            if (copy.Parent is { } parent && map.TryGetValue(parent, out var mapped))
                copy.Parent = mapped;
            return copy;
        })];
    }

    private string NextName(string name, Guid? parent)
    {
        var siblings = Document.Entities.Where(e => e.Parent == parent).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var stem = name.TrimEnd();
        var space = stem.LastIndexOf(' ');
        if (space > 0 && int.TryParse(stem[(space + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            stem = stem[..space];
        for (var i = 1; ; i++)
        {
            var candidate = $"{stem} {i}";
            if (!siblings.Contains(candidate))
                return candidate;
        }
    }

    private void SetFlag(Guid id, EntityField field, bool value, string verb)
    {
        var entity = Get(id);
        var current = field switch
        {
            EntityField.Active => entity.Active,
            EntityField.Hidden => entity.Editor.Hidden,
            _ => entity.Editor.Locked
        };
        if (current == value)
            return;
        _undo.Execute(new EntityFieldCommand(this, $"{verb} {DisplayName(entity)}", id, field, current, value));
    }

    private bool HasAncestorIn(EntityDocument entity, HashSet<Guid> set)
    {
        for (var parent = entity.Parent; parent is { } p; parent = Find(p)?.Parent)
        {
            if (set.Contains(p))
                return true;
        }

        return false;
    }

    private int SubtreeLength(int start)
    {
        var entities = Document.Entities;
        var ids = new HashSet<Guid> { entities[start].Id };
        var end = start + 1;
        while (end < entities.Count && entities[end].Parent is { } parent && ids.Contains(parent))
            ids.Add(entities[end++].Id);
        return end - start;
    }

    /// <summary>The list index where a subtree goes to become the child of <paramref name="parent"/> at <paramref name="siblingIndex"/>.</summary>
    private int InsertionIndex(Guid? parent, int siblingIndex)
    {
        var entities = Document.Entities;
        var sibling = 0;
        for (var i = 0; i < entities.Count; i++)
        {
            if (entities[i].Parent != parent)
                continue;
            if (sibling++ == siblingIndex)
                return i;
        }

        if (parent is null)
            return entities.Count;
        var parentIndex = entities.FindIndex(e => e.Id == parent);
        return parentIndex + SubtreeLength(parentIndex);
    }

    private void Reindex()
    {
        _byId.Clear();
        foreach (var entity in Document.Entities)
            _byId[entity.Id] = entity;
    }

    private void Raise(SceneChange change) => Changed?.Invoke(this, new SceneChangedEventArgs(change));

    internal void RawInsert(SubtreeBlock block)
    {
        var index = InsertionIndex(block.Parent, block.SiblingIndex);
        var copies = block.Entities.ConvertAll(e => e.Clone());
        Document.Entities.InsertRange(index, copies);
        foreach (var entity in copies)
            _byId[entity.Id] = entity;
        foreach (var entity in copies)
            Raise(new SceneChange(SceneChangeKind.EntityAdded, entity.Id) { NewParent = entity.Parent });
    }

    internal void RawRemove(Guid root)
    {
        var start = Document.Entities.FindIndex(e => e.Id == root);
        if (start < 0)
            return;
        var length = SubtreeLength(start);
        var removed = Document.Entities.GetRange(start, length);
        Document.Entities.RemoveRange(start, length);
        foreach (var entity in removed)
            _byId.Remove(entity.Id);
        for (var i = removed.Count - 1; i >= 0; i--)
            Raise(new SceneChange(SceneChangeKind.EntityRemoved, removed[i].Id));
    }

    internal void RawMove(Guid id, Guid? parent, int siblingIndex)
    {
        var start = Document.Entities.FindIndex(e => e.Id == id);
        var length = SubtreeLength(start);
        var block = Document.Entities.GetRange(start, length);
        var oldParent = block[0].Parent;
        Document.Entities.RemoveRange(start, length);
        block[0].Parent = parent;
        Document.Entities.InsertRange(InsertionIndex(parent, siblingIndex), block);
        Raise(new SceneChange(SceneChangeKind.EntityMoved, id) { OldParent = oldParent, NewParent = parent });
    }

    internal void RawSetField(Guid id, EntityField field, object value)
    {
        var entity = Get(id);
        switch (field)
        {
            case EntityField.Name:
                entity.Name = (string)value;
                Raise(new SceneChange(SceneChangeKind.EntityRenamed, id));
                return;
            case EntityField.Active:
                entity.Active = (bool)value;
                break;
            case EntityField.Hidden:
                entity.Editor.Hidden = (bool)value;
                break;
            case EntityField.Locked:
                entity.Editor.Locked = (bool)value;
                break;
        }

        Raise(new SceneChange(SceneChangeKind.EntityStateChanged, id) { Property = field.ToString().ToLowerInvariant() });
    }

    internal void RawAddComponent(Guid id, ComponentDocument component, int index)
    {
        var entity = Get(id);
        entity.Components.Insert(Math.Clamp(index, 0, entity.Components.Count), component.Clone());
        Raise(new SceneChange(SceneChangeKind.ComponentAdded, id) { Component = component.Type });
    }

    internal void RawRemoveComponent(Guid id, string type)
    {
        var entity = Get(id);
        if (entity.Components.RemoveAll(c => c.Type == type) > 0)
            Raise(new SceneChange(SceneChangeKind.ComponentRemoved, id) { Component = type });
    }

    internal void RawSetProperty(Guid id, string component, string path, JsonNode? value)
    {
        var document = Get(id).FindComponent(component) ?? throw new KeyNotFoundException($"The entity has no {component} component.");
        if (path.Length == 0)
            document.Data = (JsonObject)(value?.DeepClone() ?? new JsonObject());
        else if (value is null && path.IndexOf('.') < 0)
            document.Data.Remove(path);
        else
            JsonPaths.Set(document.Data, path, value?.DeepClone());
        Raise(new SceneChange(SceneChangeKind.PropertyChanged, id) { Component = component, Property = path });
    }

    internal void RawReplace(EntityDocument entity)
    {
        var index = Document.Entities.FindIndex(e => e.Id == entity.Id);
        var copy = entity.Clone();
        Document.Entities[index] = copy;
        _byId[copy.Id] = copy;
        Raise(new SceneChange(SceneChangeKind.EntityReplaced, copy.Id));
    }

    internal void RawSetEnvironment(SceneEnvironment environment)
    {
        Document.Environment = environment.Clone();
        Raise(new SceneChange(SceneChangeKind.EnvironmentChanged, Guid.Empty));
    }
}
