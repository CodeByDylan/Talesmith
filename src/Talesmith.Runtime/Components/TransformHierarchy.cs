using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.Runtime.Components;

/// <summary>Computes the world <see cref="Transform"/> of child entities from their <see cref="Parent"/> and <see cref="LocalTransform"/>.</summary>
/// <remarks>
/// Children of destroyed parents keep their last transform; parent chains that loop are treated as roots. Not thread-safe.
/// </remarks>
public sealed class TransformHierarchy
{
    /// <summary>Deeper chains are treated as loops.</summary>
    public const int MaxDepth = 1024;

    private static readonly QueryDescription Children = QueryDescription.With<Parent>().And<LocalTransform>();

    private Entity[] _entities = [];
    private int[] _depths = [];
    private int[] _depthById = [];
    private int[] _stampById = [];
    private int[] _counts = [];
    private Entity[] _sorted = [];
    private readonly List<Entity> _chain = [];
    private int _stamp;

    /// <summary>Updates the world transform of every child entity.</summary>
    public void Update(World world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var query = world.Query(Children);
        var count = query.Count;
        if (count == 0)
            return;

        if (_entities.Length < count)
        {
            _entities = new Entity[count * 2];
            _depths = new int[count * 2];
            _sorted = new Entity[count * 2];
        }

        var n = 0;
        foreach (var archetype in query)
        {
            foreach (var entity in archetype.Entities)
                _entities[n++] = entity;
        }

        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_stampById);
            _stamp = 1;
        }

        var maxDepth = 0;
        for (var i = 0; i < n; i++)
        {
            _depths[i] = DepthOf(world, _entities[i]);
            maxDepth = Math.Max(maxDepth, _depths[i]);
        }

        SortByDepth(n, maxDepth);
        for (var i = 0; i < n; i++)
        {
            var entity = _sorted[i];
            var parent = world.Get<Parent>(entity).Value;
            ref var local = ref world.Get<LocalTransform>(entity);
            if (!world.IsAlive(parent) || parent == entity)
                continue;
            ref var parentTransform = ref world.TryGetRef<Transform>(parent, out var hasParentTransform);
            var composed = hasParentTransform ? Compose(parentTransform, local) : local.ToTransform();
            ref var transform = ref world.TryGetRef<Transform>(entity, out var hasTransform);
            if (hasTransform)
                transform = composed;
            else
                world.Set(entity, composed);
        }
    }

    /// <summary>The world transform of a child placed at <paramref name="local"/> under a parent at <paramref name="parent"/>.</summary>
    public static Transform Compose(in Transform parent, in LocalTransform local)
    {
        var offset = Rotate(local.Position * parent.Scale, parent.Rotation);
        return new Transform(parent.Position + offset, parent.Rotation + local.Rotation) { Scale = parent.Scale * local.Scale };
    }

    /// <summary>The local transform that places a child at <paramref name="world"/> under a parent at <paramref name="parent"/>.</summary>
    public static LocalTransform Decompose(in Transform parent, in Transform world)
    {
        var scale = new Vector2(parent.Scale.X == 0 ? 0 : 1 / parent.Scale.X, parent.Scale.Y == 0 ? 0 : 1 / parent.Scale.Y);
        var position = Rotate(world.Position - parent.Position, -parent.Rotation) * scale;
        return new LocalTransform(position, world.Rotation - parent.Rotation) { Scale = world.Scale * scale };
    }

    private static Vector2 Rotate(Vector2 value, float radians)
    {
        if (radians == 0)
            return value;
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
    }

    private int DepthOf(World world, Entity entity)
    {
        _chain.Clear();
        var current = entity;
        var depth = 0;
        while (true)
        {
            if (Known(current.Id, out var known))
            {
                depth = known;
                break;
            }

            ref var parent = ref world.TryGetRef<Parent>(current, out var hasParent);
            if (!hasParent)
            {
                depth = 0;
                break;
            }

            if (!world.IsAlive(parent.Value) || _chain.Count >= MaxDepth)
            {
                _chain.Add(current);
                depth = -1;
                break;
            }

            _chain.Add(current);
            current = parent.Value;
        }

        for (var i = _chain.Count - 1; i >= 0; i--)
        {
            depth++;
            Remember(_chain[i].Id, depth);
        }

        return Known(entity.Id, out var result) ? result : 0;
    }

    private bool Known(int id, out int depth)
    {
        if (id < _stampById.Length && _stampById[id] == _stamp)
        {
            depth = _depthById[id];
            return true;
        }

        depth = 0;
        return false;
    }

    private void Remember(int id, int depth)
    {
        if (id >= _stampById.Length)
        {
            var size = Math.Max(id + 1, _stampById.Length * 2);
            Array.Resize(ref _stampById, size);
            Array.Resize(ref _depthById, size);
        }

        _stampById[id] = _stamp;
        _depthById[id] = depth;
    }

    private void SortByDepth(int n, int maxDepth)
    {
        if (_counts.Length < maxDepth + 2)
            _counts = new int[maxDepth + 2];
        else
            Array.Clear(_counts, 0, maxDepth + 2);
        for (var i = 0; i < n; i++)
            _counts[_depths[i] + 1]++;
        for (var d = 1; d <= maxDepth + 1; d++)
            _counts[d] += _counts[d - 1];
        for (var i = 0; i < n; i++)
            _sorted[_counts[_depths[i]]++] = _entities[i];
    }
}

/// <summary>Builds and changes entity hierarchies.</summary>
public static class HierarchyExtensions
{
    /// <summary>Makes <paramref name="child"/> a child of <paramref name="parent"/>, keeping where it is in the world unless told otherwise.</summary>
    /// <param name="keepWorldTransform">When false, the child's current transform becomes its local transform.</param>
    public static void SetParent(this World world, Entity child, Entity parent, bool keepWorldTransform = true)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (parent.IsNull)
        {
            world.ClearParent(child);
            return;
        }

        var transform = world.TryGet<Transform>(child, out var current) ? current : new Transform();
        var local = keepWorldTransform && world.TryGet<Transform>(parent, out var parentTransform)
            ? TransformHierarchy.Decompose(parentTransform, transform)
            : new LocalTransform(transform);
        world.Set(child, new Parent(parent));
        world.Set(child, local);
        world.Set(child, world.TryGet<Transform>(parent, out var p) ? TransformHierarchy.Compose(p, local) : transform);
    }

    /// <summary>Makes an entity a root, keeping its world transform.</summary>
    public static void ClearParent(this World world, Entity child)
    {
        ArgumentNullException.ThrowIfNull(world);
        world.Remove<Parent>(child);
        world.Remove<LocalTransform>(child);
    }

    /// <summary>The entity's parent, or <see cref="Entity.Null"/> for roots.</summary>
    public static Entity GetParent(this World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.TryGet<Parent>(entity, out var parent) ? parent.Value : Entity.Null;
    }

    /// <summary>Adds the direct children of an entity to <paramref name="children"/>.</summary>
    public static void GetChildren(this World world, Entity entity, List<Entity> children)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(children);
        foreach (var archetype in world.Query<Parent>())
        {
            var parents = archetype.GetSpan<Parent>();
            var entities = archetype.Entities;
            for (var i = 0; i < parents.Length; i++)
            {
                if (parents[i].Value == entity)
                    children.Add(entities[i]);
            }
        }
    }

    /// <summary>Destroys an entity and all of its descendants.</summary>
    public static void DestroyWithChildren(this World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        var pending = new List<Entity> { entity };
        for (var i = 0; i < pending.Count; i++)
            world.GetChildren(pending[i], pending);
        foreach (var item in pending)
            world.Destroy(item);
    }
}
