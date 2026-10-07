using System.Numerics;

namespace Talesmith.Runtime.Components;

/// <summary>An area that reports when activators enter or leave it, such as a map trigger polygon.</summary>
/// <param name="Polygon">Vertices relative to the entity's <see cref="Transform"/> position.</param>
public sealed record TriggerArea(string Name, IReadOnlyList<Vector2> Polygon, Assets.Maps.PropertySet Properties)
{
    public bool Contains(Vector2 local)
    {
        var inside = false;
        for (int i = 0, j = Polygon.Count - 1; i < Polygon.Count; j = i++)
        {
            var a = Polygon[i];
            var b = Polygon[j];
            if ((a.Y > local.Y) != (b.Y > local.Y) && local.X < (b.X - a.X) * (local.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}

/// <summary>Marks an entity, such as the player, whose position activates <see cref="TriggerArea"/>s.</summary>
public struct TriggerActivator
{
    /// <summary>The trigger entity currently containing the activator, if any.</summary>
    public Ecs.Entity Inside;
}
