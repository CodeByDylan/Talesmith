using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Ecs;
using Talesmith.Lighting;
using Talesmith.Physics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>Draws collider outlines, solid for colliders and dashed and tinted for trigger areas, with handles for a box's or capsule's size, a
/// circle's radius and a polygon's points.</summary>
public sealed class ColliderGizmo : IGizmoProvider
{
    private static readonly QueryDescription Colliders = QueryDescription.With<Collider2D>().And<Transform>().Without<Inactive>();
    private readonly string _type = ComponentRegistry.GetTypeName(typeof(Collider2D));

    public void Draw(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Span<Vector2> outline = stackalloc Vector2[64];
        foreach (var archetype in context.World.Query(Colliders))
        {
            var colliders = archetype.GetSpan<Collider2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                ref readonly var collider = ref colliders[i];
                var buffer = collider.Points is { Length: > 64 } points ? new Vector2[points.Length] : outline;
                var count = ColliderGeometry.GetOutline(collider, transforms[i], buffer);
                if (count < 2)
                    continue;
                var entity = archetype.Entities[i];
                var selected = context.IsSelected(entity);
                var opacity = selected ? 1 : context.IsHovered(entity) ? 0.75 : 0.45;
                var color = collider.IsTrigger ? context.Palette.Trigger : context.Palette.Collider;
                context.Polyline(buffer[..count], color, closed: true, selected ? 1.75 : 1.25, opacity, dashed: collider.IsTrigger,
                    fillOpacity: collider.IsTrigger ? 0.08 : selected ? 0.05 : 0);
            }
        }
    }

    public void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handles);
        foreach (var archetype in context.World.Query(Colliders))
        {
            var colliders = archetype.GetSpan<Collider2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                if (!context.IsSelected(entity))
                    continue;
                var id = GizmoMath.DocumentId(context.World, entity);
                if (id != Guid.Empty)
                    ShapeHandles.Add(handles, id, _type, colliders[i].Shape switch
                    {
                        ColliderShape.Circle => ShapeKind.Circle,
                        ColliderShape.Polygon => ShapeKind.Polygon,
                        _ => ShapeKind.Box
                    }, transforms[i], colliders[i].Offset, colliders[i].Rotation, colliders[i].Size, colliders[i].Radius, colliders[i].Points,
                        colliders[i].IsTrigger ? context.Palette.Trigger : context.Palette.Collider);
            }
        }
    }
}

/// <summary>Draws the outlines of shadow casters, dashed in violet, with handles for their size, radius or points.</summary>
public sealed class ShadowCasterGizmo : IGizmoProvider
{
    private static readonly QueryDescription Casters = QueryDescription.With<ShadowCaster2D>().And<Transform>().Without<Inactive>();
    private readonly string _type = ComponentRegistry.GetTypeName(typeof(ShadowCaster2D));

    public void Draw(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Span<Vector2> points = stackalloc Vector2[48];
        foreach (var archetype in context.World.Query(Casters))
        {
            var casters = archetype.GetSpan<ShadowCaster2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                var selected = context.IsSelected(entity);
                if (!selected && !context.HasSelection && !context.IsHovered(entity))
                    continue;
                var count = Outline(casters[i], transforms[i], points);
                if (count >= 2)
                    context.Polyline(points[..count], context.Palette.Shadow, closed: true, 1.5, selected ? 1 : 0.4, dashed: true, fillOpacity: selected ? 0.1 : 0);
            }
        }
    }

    public void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handles);
        foreach (var archetype in context.World.Query(Casters))
        {
            var casters = archetype.GetSpan<ShadowCaster2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                if (!context.IsSelected(entity) || GizmoMath.DocumentId(context.World, entity) is var id && id == Guid.Empty)
                    continue;
                var caster = casters[i];
                ShapeHandles.Add(handles, id, _type, caster.Shape switch
                {
                    ShadowCasterShape.Circle => ShapeKind.Circle,
                    ShadowCasterShape.Polygon => ShapeKind.Polygon,
                    _ => ShapeKind.Box
                }, transforms[i], caster.Offset, 0, caster.Size, caster.Radius, caster.Points, context.Palette.Shadow);
            }
        }
    }

    private static int Outline(in ShadowCaster2D caster, in Transform transform, Span<Vector2> points)
    {
        switch (caster.Shape)
        {
            case ShadowCasterShape.Circle:
                for (var i = 0; i < 32; i++)
                    points[i] = GizmoMath.ToWorld(transform, caster.Offset + caster.Radius * GizmoMath.Direction(MathF.Tau * i / 32));
                return 32;
            case ShadowCasterShape.Polygon when caster.Points is { Length: >= 2 and <= 48 } polygon:
                for (var i = 0; i < polygon.Length; i++)
                    points[i] = GizmoMath.ToWorld(transform, caster.Offset + polygon[i]);
                return polygon.Length;
            case ShadowCasterShape.Box:
                var half = caster.Size / 2;
                points[0] = GizmoMath.ToWorld(transform, caster.Offset + new Vector2(-half.X, -half.Y));
                points[1] = GizmoMath.ToWorld(transform, caster.Offset + new Vector2(half.X, -half.Y));
                points[2] = GizmoMath.ToWorld(transform, caster.Offset + new Vector2(half.X, half.Y));
                points[3] = GizmoMath.ToWorld(transform, caster.Offset + new Vector2(-half.X, half.Y));
                return 4;
            default:
                return 0;
        }
    }
}

internal enum ShapeKind
{
    Box,
    Circle,
    Polygon
}

/// <summary>Handles for shapes given in an entity's local space with an offset and rotation: size handles on a box's right and bottom edges, a
/// radius handle on a circle, and a handle per polygon point.</summary>
internal static class ShapeHandles
{
    public static void Add(ICollection<GizmoHandle> handles, Guid id, string component, ShapeKind kind, Transform transform, Vector2 offset, float rotation,
        Vector2 size, float radius, IReadOnlyList<Vector2>? points, global::Avalonia.Media.Color color, string prefix = "")
    {
        Vector2 ToWorld(Vector2 local) => GizmoMath.ToWorld(transform, offset + GizmoMath.Rotate(local, rotation));
        Vector2 ToShape(Vector2 world) => GizmoMath.Rotate(GizmoMath.ToLocal(transform, world) - offset, -rotation);

        switch (kind)
        {
            case ShapeKind.Box:
                handles.Add(new GizmoHandle(id, component, prefix + "size", ToWorld(new Vector2(size.X / 2, 0)), drag =>
                {
                    var current = GizmoMath.ReadVector(drag.StartValue, size);
                    return GizmoMath.Write(new Vector2(GizmoMath.Snap(MathF.Abs(ToShape(drag.World).X) * 2, 2, drag.Snap), current.Y));
                })
                {
                    Color = color,
                    Key = "width",
                    Hint = "Drag to change the width; Ctrl snaps",
                    Cursor = global::Avalonia.Input.StandardCursorType.SizeWestEast
                });
                handles.Add(new GizmoHandle(id, component, prefix + "size", ToWorld(new Vector2(0, size.Y / 2)), drag =>
                {
                    var current = GizmoMath.ReadVector(drag.StartValue, size);
                    return GizmoMath.Write(new Vector2(current.X, GizmoMath.Snap(MathF.Abs(ToShape(drag.World).Y) * 2, 2, drag.Snap)));
                })
                {
                    Color = color,
                    Key = "height",
                    Hint = "Drag to change the height; Ctrl snaps",
                    Cursor = global::Avalonia.Input.StandardCursorType.SizeNorthSouth
                });
                break;
            case ShapeKind.Circle:
                handles.Add(new GizmoHandle(id, component, prefix + "radius", ToWorld(new Vector2(radius, 0)),
                    drag => JsonValue.Create(GizmoMath.Snap(ToShape(drag.World).Length(), 1, drag.Snap)))
                {
                    Shape = GizmoHandleShape.Circle,
                    Color = color,
                    Key = "radius",
                    Hint = "Drag to change the radius; Ctrl snaps"
                });
                break;
            case ShapeKind.Polygon when points is not null:
                for (var i = 0; i < points.Count; i++)
                {
                    var index = i;
                    handles.Add(new GizmoHandle(id, component, $"{prefix}points.{i}", ToWorld(points[i]), drag =>
                    {
                        var local = ToShape(drag.World);
                        return GizmoMath.Write(new Vector2(GizmoMath.Snap(local.X, 4, drag.Snap), GizmoMath.Snap(local.Y, 4, drag.Snap)));
                    })
                    {
                        Shape = GizmoHandleShape.Circle,
                        Color = color,
                        Key = $"point{index}",
                        Hint = "Drag to move the point; Ctrl snaps"
                    });
                }

                break;
        }
    }
}
