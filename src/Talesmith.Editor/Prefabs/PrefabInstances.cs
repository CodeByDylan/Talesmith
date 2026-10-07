using Talesmith.Assets;
using Talesmith.Editor.Documents;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Prefabs;

/// <summary>An entity of a prefab instance in the open scene, as expanded from its prefab.</summary>
/// <param name="Instance">The scene's instance entity, which holds the prefab link and overrides.</param>
/// <param name="Entity">The entity with the instance's overrides applied.</param>
/// <param name="Base">The entity as the prefab has it, without the instance's changes, or null when the instance added it.</param>
public sealed record InstanceMember(EntityDocument Instance, ExpandedEntity Entity, ExpandedEntity? Base)
{
    /// <summary>The id in the scene, as the runtime and the selection use it.</summary>
    public Guid Id => Entity.Id;

    /// <summary>The id within the prefab, which overrides address.</summary>
    public Guid PrefabId => Entity.PrefabId ?? Entity.Id;

    /// <summary>Whether this is the instance entity itself, which the scene stores.</summary>
    public bool IsRoot => Entity.Id == Instance.Id;
}

/// <summary>Finds the entities of prefab instances in the open scene: those the scene stores only as a prefab link.</summary>
/// <remarks>Results are cached until the scene or a prefab changes. Use it on the UI thread.</remarks>
public sealed class PrefabInstances
{
    private readonly ISceneDocumentService _documents;
    private readonly PrefabLibrary _library;
    private readonly Dictionary<Guid, InstanceMember> _byId = [];
    private readonly Dictionary<Guid, IReadOnlyList<InstanceMember>> _byInstance = [];
    private readonly Dictionary<Guid, HashSet<AssetGuid>> _uses = [];
    private SceneDocumentModel? _model;
    private bool _valid;

    public PrefabInstances(ISceneDocumentService documents, PrefabLibrary library)
    {
        _documents = documents;
        _library = library;
        documents.ActiveChanged += (_, _) => Track();
        library.Changed += (_, _) => Invalidate();
        Track();
    }

    /// <summary>Raised after cached instances were dropped because the scene's instances or a prefab changed.</summary>
    public event EventHandler? Invalidated;

    /// <summary>The members of a scene instance, its root first, or empty when the entity is no instance or its prefab is missing.</summary>
    public IReadOnlyList<InstanceMember> GetMembers(Guid instance)
    {
        Build();
        return _byInstance.GetValueOrDefault(instance) ?? [];
    }

    /// <summary>The instance member with a scene id, including instance roots; null for plain entities.</summary>
    public InstanceMember? Find(Guid id)
    {
        Build();
        return _byId.GetValueOrDefault(id);
    }

    /// <summary>Whether the entity exists only through a prefab instance, so the scene does not store it.</summary>
    public bool IsVirtual(Guid id) => Find(id) is { IsRoot: false };

    /// <summary>The scene instances whose prefab is <paramref name="prefab"/> or contains it.</summary>
    public IReadOnlyList<Guid> InstancesUsing(AssetGuid prefab)
    {
        Build();
        return [.. _uses.Where(p => p.Value.Contains(prefab)).Select(p => p.Key)];
    }

    /// <summary>Whether a member's component property differs from the prefab.</summary>
    public static bool IsOverridden(InstanceMember member, string component, string path)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.Instance.Prefab is not { } link)
            return false;
        if (member.IsRoot && member.Instance.FindComponent(component) is { } own)
        {
            var baseData = member.Base?.Components.FirstOrDefault(c => c.Type == component)?.Data;
            if (baseData is null)
                return true;
            return !System.Text.Json.Nodes.JsonNode.DeepEquals(Get(own.Data, path), Get(baseData, path));
        }

        var id = member.PrefabId;
        return link.Overrides.Exists(o => o.Entity == id && o.Component == component && (o.Path == path || o.Path.StartsWith(path + ".", StringComparison.Ordinal) ||
                                                                                          path.StartsWith(o.Path + ".", StringComparison.Ordinal)));
    }

    /// <summary>Whether the member has any change against its prefab.</summary>
    public static bool HasOverrides(InstanceMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (member.Instance.Prefab is not { } link)
            return false;
        if (member.IsRoot)
            return link.Overrides.Count > 0 || link.RemovedComponents.Count > 0 || member.Instance.Components.Exists(c => c.Type != "Transform");
        return link.Overrides.Exists(o => o.Entity == member.PrefabId) || link.RemovedComponents.Exists(r => r.Entity == member.PrefabId);
    }

    private static System.Text.Json.Nodes.JsonNode? Get(System.Text.Json.Nodes.JsonObject data, string path) => path.Length == 0 ? data : JsonPaths.Get(data, path);

    private void Track()
    {
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = _documents.Active;
        if (_model is not null)
            _model.Changed += OnSceneChanged;
        Invalidate();
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        var change = e.Change;
        if (change.Kind is SceneChangeKind.EnvironmentChanged)
            return;
        if (change.Kind is SceneChangeKind.PropertyChanged or SceneChangeKind.EntityRenamed or SceneChangeKind.EntityStateChanged or SceneChangeKind.ComponentAdded
                or SceneChangeKind.ComponentRemoved && _model?.Find(change.Entity) is { Prefab: null })
            return;
        Invalidate();
    }

    private void Invalidate()
    {
        _valid = false;
        _byId.Clear();
        _byInstance.Clear();
        _uses.Clear();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void Build()
    {
        if (!ReferenceEquals(_model, _documents.Active))
            Track();
        if (_valid || _model is not { } model)
            return;
        _valid = true;
        foreach (var entity in model.Entities)
        {
            if (entity.Prefab is null)
                continue;
            var used = new HashSet<AssetGuid>();
            var expanded = PrefabExpansion.ExpandInstance(entity, _library.Get, applyInstanceChanges: true, used);
            var bases = PrefabExpansion.ExpandInstance(entity, _library.Get, applyInstanceChanges: false).ToDictionary(e => e.Id);
            var members = expanded.Select(e => new InstanceMember(entity, e, bases.GetValueOrDefault(e.Id))).ToList();
            _byInstance[entity.Id] = members;
            _uses[entity.Id] = used;
            foreach (var member in members)
                _byId.TryAdd(member.Id, member);
        }
    }
}
