using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Transform = Talesmith.Runtime.Components.Transform;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>An entity a transform tool moves, with its world transform and its parent's when the drag started.</summary>
internal readonly record struct TransformedEntity(Guid Id, Transform Start, Transform? Parent);

/// <summary>The entities a transform tool works on: the selected entities that are not children of other selected ones, with the pivot handles
/// sit on and the axes they follow.</summary>
internal sealed class TransformSelection
{
    private TransformSelection(IReadOnlyList<TransformedEntity> entities, Vector2 pivot, float rotation, Rect2 bounds)
    {
        Entities = entities;
        Pivot = pivot;
        Rotation = rotation;
        Bounds = bounds;
    }

    public IReadOnlyList<TransformedEntity> Entities { get; }

    /// <summary>Where the handles are: the primary entity's position, or the center of the selection's bounds.</summary>
    public Vector2 Pivot { get; }

    /// <summary>The handles' rotation: the primary entity's in local space, otherwise 0.</summary>
    public float Rotation { get; }

    /// <summary>The axis-aligned world bounds of the entities' visuals, or of their positions.</summary>
    public Rect2 Bounds { get; }

    public Vector2 AxisX => GizmoMath.Direction(Rotation);

    public Vector2 AxisY => GizmoMath.Direction(Rotation + MathF.PI / 2);

    /// <summary>The movable selected entities as they are now, or null when there are none.</summary>
    public static TransformSelection? Capture(ViewportToolContext context, EntityDataService entities)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(entities);
        if (context.World.World is not { } world || context.Selection.Entities.Count == 0)
            return null;
        var selected = context.Selection.Entities.Where(entities.Exists).ToHashSet();
        var list = new List<TransformedEntity>();
        foreach (var id in context.Selection.Entities)
        {
            if (!selected.Contains(id) || !context.World.TryGetEntity(id, out var entity) || !world.TryGet<Transform>(entity, out var transform))
                continue;
            if (context.Document?.Find(id) is { Editor.Locked: true } || entities.GetComponent(id, "Transform") is null || HasSelectedAncestor(world, entity, selected))
                continue;
            Transform? parent = world.TryGet<Parent>(entity, out var link) && world.IsAlive(link.Value) && world.TryGet<Transform>(link.Value, out var parentTransform)
                ? parentTransform
                : null;
            list.Add(new TransformedEntity(id, transform, parent));
        }

        if (list.Count == 0)
            return null;
        var positions = list.Select(e => e.Start.Position).ToList();
        var bounds = context.Picker.GetBounds(list.Select(e => e.Id), context.Camera.Zoom) ?? BoundsOf(positions);
        var pivot = context.Options.Pivot == PivotMode.Center ? new Vector2(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) : list[^1].Start.Position;
        var rotation = context.Options.Space == HandleSpace.Local ? list[^1].Start.Rotation : 0;
        return new TransformSelection(list, pivot, rotation, bounds);
    }

    /// <summary>Writes new world transforms as the entities' local transforms; only the parts asked for are written.</summary>
    public void Apply(EntityDataService entities, Func<TransformedEntity, Transform> transform, bool position, bool rotation, bool scale)
    {
        ArgumentNullException.ThrowIfNull(entities);
        foreach (var entity in Entities)
        {
            var world = transform(entity);
            var local = entity.Parent is { } parent ? TransformHierarchy.Decompose(parent, world) : new LocalTransform(world);
            if (position)
                entities.Set(entity.Id, "Transform", "position", GizmoMath.Write(local.Position));
            if (rotation)
                entities.Set(entity.Id, "Transform", "rotation", MathF.Round(local.Rotation, 5));
            if (scale)
                entities.Set(entity.Id, "Transform", "scale", GizmoMath.Write(local.Scale));
        }
    }

    private static bool HasSelectedAncestor(World world, Entity entity, HashSet<Guid> selected)
    {
        for (var current = Parent(world, entity); !current.IsNull; current = Parent(world, current))
        {
            if (world.TryGet<SceneEntityId>(current, out var id) && selected.Contains(id.Value))
                return true;
        }

        return false;
    }

    private static Entity Parent(World world, Entity entity) =>
        world.IsAlive(entity) && world.TryGet<Parent>(entity, out var parent) ? parent.Value : Entity.Null;

    private static Rect2 BoundsOf(List<Vector2> points)
    {
        var min = points.Aggregate(Vector2.Min);
        var max = points.Aggregate(Vector2.Max);
        return new Rect2(min.X, min.Y, max.X - min.X, max.Y - min.Y);
    }
}
