using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Ecs;
using Talesmith.Lighting;
using Talesmith.Rendering.Lighting;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>Draws lights: the radius and inner radius of point lights, the cone of spot lights and the direction of directional lights, with handles
/// for the radius, inner radius and cone angle.</summary>
public sealed class LightGizmo : IGizmoProvider
{
    private static readonly QueryDescription Lights = QueryDescription.With<Light2D>().And<Transform>().Without<Inactive>();
    private readonly string _type = ComponentRegistry.GetTypeName(typeof(Light2D));

    public void Draw(GizmoContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var color = context.Palette.Light;
        foreach (var archetype in context.World.Query(Lights))
        {
            var lights = archetype.GetSpan<Light2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                var selected = context.IsSelected(entity);
                var opacity = selected ? 1 : context.IsHovered(entity) ? 0.7 : GizmoContext.Faint;
                ref readonly var light = ref lights[i];
                var position = transforms[i].Position;
                var rotation = transforms[i].Rotation;
                switch (light.Type)
                {
                    case LightType.Spot:
                        Cone(context, position, rotation, light.Radius, light.SpotAngle, color, opacity, dashed: false);
                        if (selected && light.SpotInnerAngle > 0)
                            Cone(context, position, rotation, light.Radius * MathF.Max(light.InnerRadius, 0.35f), light.SpotInnerAngle, color, opacity * 0.8, dashed: true);
                        break;
                    case LightType.Directional:
                        Arrow(context, position, rotation, color, opacity);
                        break;
                    default:
                        context.Circle(position, light.Radius, color, selected ? 1.5 : 1.25, opacity);
                        if (selected && light.InnerRadius > 0)
                            context.Circle(position, light.Radius * light.InnerRadius, color, 1.25, opacity * 0.8, dashed: true);
                        break;
                }

                context.Dot(position, color, selected ? 3.5 : 2.5, opacity);
            }
        }
    }

    public void CollectHandles(GizmoContext context, ICollection<GizmoHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(handles);
        var color = context.Palette.Light;
        foreach (var archetype in context.World.Query(Lights))
        {
            var lights = archetype.GetSpan<Light2D>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < archetype.Count; i++)
            {
                var entity = archetype.Entities[i];
                if (!context.IsSelected(entity) || lights[i].Type == LightType.Directional)
                    continue;
                var id = GizmoMath.DocumentId(context.World, entity);
                if (id == Guid.Empty)
                    continue;
                var light = lights[i];
                var position = transforms[i].Position;
                var rotation = transforms[i].Rotation;
                var direction = GizmoMath.Direction(rotation);
                handles.Add(new GizmoHandle(id, _type, "radius", position + direction * light.Radius,
                    drag => GizmoMath.Snap(MathF.Max(0, Vector2.Distance(drag.World, position)), 8, drag.Snap))
                {
                    Shape = GizmoHandleShape.Circle,
                    Color = color,
                    Hint = "Drag to change the light's radius; Ctrl snaps",
                    Key = "radius"
                });
                if (light.Radius > 0 && light.Type == LightType.Point)
                {
                    handles.Add(new GizmoHandle(id, _type, "innerRadius", position + direction * light.Radius * light.InnerRadius,
                        drag => MathF.Round(Math.Clamp(Vector2.Distance(drag.World, position) / light.Radius, 0, 1), drag.Snap ? 1 : 3))
                    {
                        Shape = GizmoHandleShape.Diamond,
                        Color = color,
                        Hint = "Drag to change the fully lit inner radius",
                        Key = "inner"
                    });
                }

                if (light.Type == LightType.Spot)
                {
                    var edge = position + GizmoMath.Direction(rotation + light.SpotAngle / 2) * light.Radius * 0.75f;
                    handles.Add(new GizmoHandle(id, _type, "spotAngle", edge, drag =>
                    {
                        var offset = drag.World - position;
                        var half = MathF.Abs(GizmoMath.Wrap(MathF.Atan2(offset.Y, offset.X) - rotation));
                        return JsonValue.Create(MathF.Round(GizmoMath.SnapAngle(Math.Clamp(half * 2, 0, MathF.Tau), drag.Snap), 4));
                    })
                    {
                        Shape = GizmoHandleShape.Diamond,
                        Color = color,
                        Hint = "Drag to widen or narrow the cone; Ctrl snaps to 15°",
                        Key = "angle"
                    });
                }
            }
        }
    }

    private static void Cone(GizmoContext context, Vector2 position, float rotation, float radius, float angle, global::Avalonia.Media.Color color, double opacity, bool dashed)
    {
        var half = angle / 2;
        context.Line(position, position + GizmoMath.Direction(rotation - half) * radius, color, 1.5, opacity, dashed);
        context.Line(position, position + GizmoMath.Direction(rotation + half) * radius, color, 1.5, opacity, dashed);
        context.Arc(position, radius, rotation - half, angle, color, 1.5, opacity, dashed);
    }

    private static void Arrow(GizmoContext context, Vector2 position, float rotation, global::Avalonia.Media.Color color, double opacity)
    {
        var length = 56 * context.PixelSize;
        var direction = GizmoMath.Direction(rotation);
        var normal = new Vector2(-direction.Y, direction.X);
        for (var row = -1; row <= 1; row++)
        {
            var start = position + normal * (row * 12 * context.PixelSize);
            var end = start + direction * length * (row == 0 ? 1 : 0.75f);
            context.Line(start, end, color, 1.5, opacity);
            var head = 7 * context.PixelSize;
            context.Line(end, end - direction * head + normal * head * 0.6f, color, 1.5, opacity);
            context.Line(end, end - direction * head - normal * head * 0.6f, color, 1.5, opacity);
        }
    }
}
