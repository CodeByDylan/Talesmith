using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Viewport.Gizmos;

/// <summary>Geometry helpers for gizmos: rotating vectors, mapping between an entity's local space and the world, snapping and the document id
/// of a runtime entity.</summary>
public static class GizmoMath
{
    public static Vector2 Rotate(Vector2 value, float radians)
    {
        if (radians == 0)
            return value;
        var (sin, cos) = MathF.SinCos(radians);
        return new Vector2(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
    }

    public static Vector2 Direction(float radians) => new(MathF.Cos(radians), MathF.Sin(radians));

    /// <summary>A point in an entity's local space, scaled and rotated with it, in world space.</summary>
    public static Vector2 ToWorld(in Transform transform, Vector2 local) => transform.Position + Rotate(local * Scale(transform), transform.Rotation);

    /// <summary>A world point in an entity's local space.</summary>
    public static Vector2 ToLocal(in Transform transform, Vector2 world)
    {
        var scale = Scale(transform);
        return Rotate(world - transform.Position, -transform.Rotation) / new Vector2(scale.X == 0 ? 1 : scale.X, scale.Y == 0 ? 1 : scale.Y);
    }

    /// <summary>The angle between two angles, from -π to π.</summary>
    public static float Wrap(float radians)
    {
        radians %= MathF.Tau;
        return radians > MathF.PI ? radians - MathF.Tau : radians < -MathF.PI ? radians + MathF.Tau : radians;
    }

    /// <summary>Rounds to a step when snapping, otherwise to a hundredth.</summary>
    public static float Snap(float value, float step, bool snap) => snap && step > 0 ? MathF.Round(value / step) * step : MathF.Round(value, 2);

    public static float SnapAngle(float radians, bool snap, float degrees = 15) =>
        snap ? MathF.Round(radians / (degrees * MathF.PI / 180)) * (degrees * MathF.PI / 180) : radians;

    /// <summary>The document id the runtime entity was created for, which edits address; empty when it has none.</summary>
    public static Guid DocumentId(World world, Entity entity) =>
        world.TryGet<SceneEntityId>(entity, out var id) ? id.Value : Guid.Empty;

    public static JsonArray Write(Vector2 value) => JsonFormats.WriteVector2(new Vector2(MathF.Round(value.X, 3), MathF.Round(value.Y, 3)));

    public static Vector2 ReadVector(JsonNode? node, Vector2 fallback)
    {
        if (node is null)
            return fallback;
        try
        {
            return JsonFormats.ReadVector2(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            return fallback;
        }
    }

    private static Vector2 Scale(in Transform transform) => transform.Scale == Vector2.Zero ? Vector2.One : transform.Scale;
}
