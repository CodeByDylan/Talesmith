using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Scenes;

/// <summary>Turns the entities of a world back into a scene document, such as to keep the state of a play session.</summary>
/// <remarks>
/// Entities created from documents are captured with their ids from their <see cref="SceneEntityId"/>. Components are written through their
/// definitions in registration order; components without a definition are left out. Prefab instances are captured as plain entities
/// marked with their <see cref="PrefabInstance"/> component. Children are written after their parents with their local transforms.
/// </remarks>
public sealed class SceneCapture(ComponentRegistry components, AssetReferences references)
{
    private static readonly HashSet<Type> Structural = [typeof(SceneEntityId), typeof(Name), typeof(Inactive), typeof(Parent), typeof(LocalTransform)];

    public SceneDocument Capture(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var ids = new Dictionary<Entity, Guid>();
        var included = new List<Entity>();
        foreach (var archetype in world.Archetypes)
        {
            if (!archetype.Has<SceneEntityId>())
                continue;
            foreach (var entity in archetype.Entities)
            {
                ids[entity] = world.Get<SceneEntityId>(entity).Value;
                included.Add(entity);
            }
        }

        included.Sort((a, b) => a.Id.CompareTo(b.Id));
        var context = new CaptureContext(world, ids, references);
        var document = new SceneDocument();
        var children = new Dictionary<Entity, List<Entity>>();
        var roots = new List<Entity>();
        foreach (var entity in included)
        {
            var parent = world.TryGet<Parent>(entity, out var p) && ids.ContainsKey(p.Value) ? p.Value : Entity.Null;
            if (parent.IsNull)
                roots.Add(entity);
            else if (children.TryGetValue(parent, out var list))
                list.Add(entity);
            else
                children[parent] = [entity];
        }

        var pending = new Stack<Entity>();
        for (var i = roots.Count - 1; i >= 0; i--)
            pending.Push(roots[i]);
        while (pending.Count > 0)
        {
            var entity = pending.Pop();
            document.Entities.Add(CaptureEntity(world, entity, context));
            if (children.TryGetValue(entity, out var list))
            {
                for (var i = list.Count - 1; i >= 0; i--)
                    pending.Push(list[i]);
            }
        }

        return document;
    }

    /// <summary>Captures one entity; references to other entities resolve through their <see cref="SceneEntityId"/>.</summary>
    public EntityDocument CaptureEntity(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        return CaptureEntity(world, entity, new CaptureContext(world, null, references));
    }

    private EntityDocument CaptureEntity(World world, Entity entity, CaptureContext context)
    {
        var document = new EntityDocument
        {
            Id = context.GetEntityId(entity),
            Name = world.TryGet<Name>(entity, out var name) ? name.Value : "",
            Active = !world.Has<Inactive>(entity)
        };
        if (world.TryGet<Parent>(entity, out var parent) && context.GetEntityId(parent.Value) is var parentId && parentId != Guid.Empty)
            document.Parent = parentId;

        var definitions = new List<IComponentDefinition>();
        foreach (var id in world.GetComponentIds(entity))
        {
            var type = ComponentType.FromId(id).Type;
            if (!Structural.Contains(type) && components.TryGet(type, out var definition))
                definitions.Add(definition);
        }

        definitions.Sort((a, b) => Order(a).CompareTo(Order(b)));
        foreach (var definition in definitions)
        {
            if (definition.Capture(world, entity, context) is { } data)
                document.Components.Add(new ComponentDocument(definition.TypeName, data));
        }

        return document;
    }

    private int Order(IComponentDefinition definition)
    {
        var all = components.Definitions;
        for (var i = 0; i < all.Count; i++)
        {
            if (ReferenceEquals(all[i], definition))
                return i;
        }

        return int.MaxValue;
    }

    private sealed class CaptureContext(World world, Dictionary<Entity, Guid>? ids, AssetReferences references) : ICaptureContext
    {
        public AssetGuid GetGuid(object asset) => references.GetGuid(asset);

        public Guid GetEntityId(Entity entity) => ids is not null
            ? ids.GetValueOrDefault(entity)
            : world.IsAlive(entity) && world.TryGet<SceneEntityId>(entity, out var id) ? id.Value : Guid.Empty;
    }
}
