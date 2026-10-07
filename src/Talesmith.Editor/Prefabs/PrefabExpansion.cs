using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Prefabs;

/// <summary>Where an expanded entity's data is written in the document that was expanded.</summary>
/// <param name="DocumentEntity">The document's own entity: the entity itself, or the nested prefab instance it comes from.</param>
/// <param name="InnerId">For entities of a nested instance, the entity's id within that instance's prefab; null for the document's own entities.</param>
public readonly record struct PrefabSource(Guid DocumentEntity, Guid? InnerId);

/// <summary>An entity of an expanded document, with prefab instances replaced by their prefabs' entities.</summary>
/// <param name="Id">The id in the expanded document's space, as the runtime gives it.</param>
/// <param name="Prefab">For the root of a prefab instance, its prefab; otherwise empty.</param>
/// <param name="Source">Where the entity comes from in the expanded document.</param>
/// <param name="InPrefab">For entities of an instance, where the entity is written in the instance's prefab document.</param>
public sealed record ExpandedEntity(Guid Id, Guid? Parent, string Name, bool Active, IReadOnlyList<ComponentDocument> Components, AssetGuid Prefab,
    PrefabSource Source, PrefabSource? InPrefab)
{
    /// <summary>The id within the prefab of the instance the entity belongs to, which overrides address; null for plain entities.</summary>
    public Guid? PrefabId => Source.InnerId;
}

/// <summary>Expands prefab instances into the entities the runtime creates for them, the same way <c>SceneInstantiator</c> does, so the editor can
/// show and edit them.</summary>
public static class PrefabExpansion
{
    /// <summary>Expands a document's entities, given in hierarchy order; prefab instances become their prefabs' entities.</summary>
    /// <param name="used">Receives the guid of every prefab the expansion read, nested ones included.</param>
    public static IReadOnlyList<ExpandedEntity> Expand(IReadOnlyList<EntityDocument> entities, Func<AssetGuid, PrefabDocument?> load, ISet<AssetGuid>? used = null)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(load);
        var result = new List<Working>();
        ExpandInto(entities, load, [], used, result);
        return [.. result.Select(w => w.ToRecord())];
    }

    /// <summary>Expands one prefab instance in the space of the document that holds it; the first entity is the instance's root.</summary>
    /// <param name="applyInstanceChanges">False leaves out the instance's overrides, removed and own components, giving the prefab's values.</param>
    public static IReadOnlyList<ExpandedEntity> ExpandInstance(EntityDocument instance, Func<AssetGuid, PrefabDocument?> load, bool applyInstanceChanges = true,
        ISet<AssetGuid>? used = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (instance.Prefab is not { } link || load(link.Asset) is not { } prefab)
            return [];
        used?.Add(link.Asset);
        var effective = applyInstanceChanges ? instance : WithoutChanges(instance);
        var result = new List<Working>();
        ExpandInstanceInto(effective, prefab, load, [link.Asset], used, result);
        return [.. result.Select(w => w.ToRecord())];
    }

    private static EntityDocument WithoutChanges(EntityDocument instance)
    {
        var copy = instance.Clone();
        copy.Prefab = new PrefabLink { Asset = instance.Prefab!.Asset };
        copy.Components = [];
        return copy;
    }

    private static void ExpandInto(IReadOnlyList<EntityDocument> entities, Func<AssetGuid, PrefabDocument?> load, ImmutableHashSet<AssetGuid> enclosing,
        ISet<AssetGuid>? used, List<Working> result)
    {
        foreach (var document in entities)
        {
            if (document.Prefab is { } link && !enclosing.Contains(link.Asset) && load(link.Asset) is { } prefab)
            {
                used?.Add(link.Asset);
                ExpandInstanceInto(document, prefab, load, enclosing.Add(link.Asset), used, result);
                continue;
            }

            result.Add(Working.From(document, new PrefabSource(document.Id, null)));
        }
    }

    private static void ExpandInstanceInto(EntityDocument instance, PrefabDocument prefab, Func<AssetGuid, PrefabDocument?> load, ImmutableHashSet<AssetGuid> enclosing,
        ISet<AssetGuid>? used, List<Working> result)
    {
        var inner = new List<Working>();
        ExpandInto(prefab.Entities, load, enclosing, used, inner);
        var link = instance.Prefab!;
        var byId = new Dictionary<Guid, Working>(inner.Count);
        foreach (var entity in inner)
            byId.TryAdd(entity.Id, entity);

        foreach (var removed in link.RemovedComponents)
        {
            if (byId.TryGetValue(removed.Entity, out var entity))
                entity.Components.RemoveAll(c => c.Type == removed.Component);
        }

        foreach (var change in link.Overrides)
        {
            if (byId.TryGetValue(change.Entity, out var entity) && change.Path.Length > 0)
                ApplyOverride(entity, change);
        }

        if (prefab.Root?.Id is not { } root)
        {
            result.Add(Working.From(instance, new PrefabSource(instance.Id, null)));
            return;
        }

        foreach (var entity in inner)
        {
            var isRoot = entity.Id == root;
            var innerId = entity.Id;
            entity.Id = PrefabIds.Map(instance.Id, root, entity.Id);
            entity.Parent = isRoot ? instance.Parent : entity.Parent is { } parent ? PrefabIds.Map(instance.Id, root, parent) : instance.Id;
            entity.InPrefab = entity.Source;
            entity.Source = new PrefabSource(instance.Id, innerId);
            if (isRoot)
            {
                if (!string.IsNullOrEmpty(instance.Name))
                    entity.Name = instance.Name;
                entity.Active = instance.Active;
                entity.Prefab = link.Asset;
                foreach (var own in instance.Components)
                {
                    var index = entity.Components.FindIndex(c => c.Type == own.Type);
                    if (index >= 0)
                        entity.Components[index] = own.Clone();
                    else
                        entity.Components.Add(own.Clone());
                }
            }

            result.Add(entity);
        }
    }

    private static void ApplyOverride(Working entity, PrefabOverride change)
    {
        var component = entity.Components.Find(c => c.Type == change.Component);
        if (component is null)
        {
            component = new ComponentDocument(change.Component, []);
            entity.Components.Add(component);
        }

        try
        {
            JsonPaths.Set(component.Data, change.Path, change.Value?.DeepClone());
        }
        catch (FormatException)
        {
        }
    }

    private sealed class Working
    {
        public Guid Id { get; set; }

        public Guid? Parent { get; set; }

        public string Name { get; set; } = "";

        public bool Active { get; set; }

        public AssetGuid Prefab { get; set; }

        public PrefabSource Source { get; set; }

        public PrefabSource? InPrefab { get; set; }

        public List<ComponentDocument> Components { get; } = [];

        public static Working From(EntityDocument document, PrefabSource source)
        {
            var working = new Working { Id = document.Id, Parent = document.Parent, Name = document.Name, Active = document.Active, Source = source };
            working.Components.AddRange(document.Components.Select(c => new ComponentDocument(c.Type, (JsonObject)c.Data.DeepClone())));
            return working;
        }

        public ExpandedEntity ToRecord() => new(Id, Parent, Name, Active, Components, Prefab, Source, InPrefab);
    }
}
