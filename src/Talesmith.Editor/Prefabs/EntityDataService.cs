using System.Text.Json.Nodes;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Prefabs;

/// <summary>Reads and writes the components of the open scene's entities as the inspector and gizmos edit them, including entities of prefab
/// instances, whose edits become the instance's overrides.</summary>
/// <remarks>
/// Entities the scene stores are edited through <see cref="SceneDocumentModel.SetProperty"/>. Components of prefab instances that come from the
/// prefab are edited by adding or replacing overrides on the instance, and a value set back to the prefab's drops its override. Every edit is
/// undoable, and edits of one value in quick succession merge.
/// </remarks>
public sealed class EntityDataService(ISceneDocumentService documents, PrefabInstances instances, IUndoService undo)
{
    private SceneDocumentModel? Model => documents.Active;

    /// <summary>Whether the open scene stores the entity or one of its prefab instances creates it.</summary>
    public bool Exists(Guid id) => Model?.Contains(id) == true || instances.Find(id) is not null;

    /// <summary>Whether the entity is stored by the scene, so it can be renamed, reparented and get components.</summary>
    public bool IsStored(Guid id) => Model?.Contains(id) == true;

    public InstanceMember? GetMember(Guid id) => instances.Find(id);

    public string GetName(Guid id) =>
        Model?.Find(id) is { } entity ? DisplayName(entity.Name) : instances.Find(id) is { } member ? DisplayName(member.Entity.Name) : "Entity";

    public bool IsActive(Guid id) => Model?.Find(id)?.Active ?? instances.Find(id)?.Entity.Active ?? true;

    /// <summary>The entity's components as the runtime gets them: for prefab instances, the prefab's with the instance's overrides applied.</summary>
    public IReadOnlyList<ComponentDocument> GetComponents(Guid id)
    {
        if (instances.Find(id) is { } member)
            return member.Entity.Components;
        return Model?.Find(id)?.Components ?? [];
    }

    public ComponentDocument? GetComponent(Guid id, string type) => GetComponents(id).FirstOrDefault(c => c.Type == type);

    /// <summary>A value of a component, or null when the component or value is missing.</summary>
    /// <param name="path">A <see cref="JsonPaths"/> path; empty gets the whole data.</param>
    public JsonNode? Get(Guid id, string component, string path)
    {
        var data = GetComponent(id, component)?.Data;
        return data is null ? null : path.Length == 0 ? data : JsonPaths.Get(data, path);
    }

    /// <summary>Whether the component is written on the entity in the scene rather than coming from a prefab.</summary>
    public bool IsStoredComponent(Guid id, string component)
    {
        if (instances.Find(id) is { } member)
            return member.IsRoot && member.Instance.FindComponent(component) is not null;
        return Model?.Find(id)?.FindComponent(component) is not null;
    }

    /// <summary>Sets a value as an undoable edit.</summary>
    public void Set(Guid id, string component, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (Model is not { } model)
            return;
        if (instances.Find(id) is not { } member || IsStoredComponent(id, component))
        {
            if (model.Find(id)?.FindComponent(component) is not null)
                model.SetProperty(id, component, path, value);
            return;
        }

        SetOverride(model, member, component, path, value);
    }

    /// <summary>Whether a prefab instance's value differs from its prefab.</summary>
    public bool IsOverridden(Guid id, string component, string path) =>
        instances.Find(id) is { } member && PrefabInstances.IsOverridden(member, component, path);

    /// <summary>The prefab's value for an entity of a prefab instance, or null.</summary>
    public JsonNode? GetPrefabValue(Guid id, string component, string path)
    {
        var data = instances.Find(id)?.Base?.Components.FirstOrDefault(c => c.Type == component)?.Data;
        return data is null ? null : path.Length == 0 ? data : JsonPaths.Get(data, path);
    }

    /// <summary>Sets an instance's value back to its prefab's.</summary>
    public void RevertToPrefab(Guid id, string component, string path)
    {
        if (Model is not { } model || instances.Find(id) is not { Instance.Prefab: { } link } member)
            return;
        if (member.IsRoot && member.Instance.FindComponent(component) is not null)
        {
            var prefabValue = GetPrefabValue(id, component, path)?.DeepClone();
            if (path.Length > 0 || prefabValue is JsonObject)
                model.SetProperty(id, component, path, prefabValue);
            return;
        }

        var updated = link.Clone();
        var removed = updated.Overrides.RemoveAll(o => o.Entity == member.PrefabId && o.Component == component && Covers(path, o.Path));
        if (removed > 0)
            undo.Execute(new PrefabLinkCommand(model, $"Revert {ShortType(component)}{(path.Length > 0 ? "." + path : "")}", member.Instance.Id, link.Clone(), updated, null));
    }

    /// <summary>Removes a component; for components from a prefab, the instance records it as removed.</summary>
    public void RemoveComponent(Guid id, string component)
    {
        if (Model is not { } model)
            return;
        if (instances.Find(id) is not { Instance.Prefab: { } link } member || IsStoredComponent(id, component))
        {
            if (model.Contains(id))
                model.RemoveComponent(id, component);
            return;
        }

        var updated = link.Clone();
        updated.Overrides.RemoveAll(o => o.Entity == member.PrefabId && o.Component == component);
        updated.RemovedComponents.Add(new PrefabComponentRef { Entity = member.PrefabId, Component = component });
        undo.Execute(new PrefabLinkCommand(model, $"Remove {ShortType(component)}", member.Instance.Id, link.Clone(), updated, null));
    }

    private void SetOverride(SceneDocumentModel model, InstanceMember member, string component, string path, JsonNode? value)
    {
        var link = member.Instance.Prefab!;
        var updated = link.Clone();
        var entity = member.PrefabId;
        var baseData = member.Base?.Components.FirstOrDefault(c => c.Type == component)?.Data;
        if (path.Length == 0)
        {
            updated.Overrides.RemoveAll(o => o.Entity == entity && o.Component == component);
            if (value is JsonObject data)
            {
                foreach (var (key, item) in data)
                {
                    if (!JsonNode.DeepEquals(item, baseData?[key]))
                        updated.Overrides.Add(new PrefabOverride { Entity = entity, Component = component, Path = key, Value = item?.DeepClone() });
                }
            }
        }
        else if (updated.Overrides.Find(o => o.Entity == entity && o.Component == component && path.StartsWith(o.Path + ".", StringComparison.Ordinal)) is { } outer)
        {
            var container = outer.Value?.DeepClone();
            if (container is JsonObject or JsonArray)
            {
                var wrapper = new JsonObject { ["v"] = container };
                JsonPaths.Set(wrapper, "v." + path[(outer.Path.Length + 1)..], value?.DeepClone());
                outer.Value = wrapper["v"]?.DeepClone();
            }
        }
        else
        {
            updated.Overrides.RemoveAll(o => o.Entity == entity && o.Component == component && Covers(path, o.Path));
            var prefabValue = baseData is null ? null : JsonPaths.Get(baseData, path);
            if (baseData is null || !JsonNode.DeepEquals(prefabValue, value))
                updated.Overrides.Add(new PrefabOverride { Entity = entity, Component = component, Path = path, Value = value?.DeepClone() });
        }

        if (Same(link, updated))
            return;
        var name = DisplayName(member.Entity.Name);
        undo.Execute(new PrefabLinkCommand(model, $"Change {ShortType(component)}{(path.Length > 0 ? "." + path : "")} of {name}", member.Instance.Id, link.Clone(), updated,
            $"{entity:N}/{component}/{path}"));
    }

    /// <summary>Whether an override at <paramref name="overridePath"/> lies at or under <paramref name="path"/>.</summary>
    private static bool Covers(string path, string overridePath) =>
        path.Length == 0 || overridePath == path || overridePath.StartsWith(path + ".", StringComparison.Ordinal);

    private static bool Same(PrefabLink a, PrefabLink b) =>
        a.Overrides.Count == b.Overrides.Count && a.RemovedComponents.Count == b.RemovedComponents.Count &&
        a.Overrides.Zip(b.Overrides).All(p => p.First.Entity == p.Second.Entity && p.First.Component == p.Second.Component && p.First.Path == p.Second.Path &&
                                              JsonNode.DeepEquals(p.First.Value, p.Second.Value));

    internal static string DisplayName(string name) => string.IsNullOrWhiteSpace(name) ? "Entity" : name;

    private static string ShortType(string type) => type[(type.LastIndexOf('.') + 1)..];
}
