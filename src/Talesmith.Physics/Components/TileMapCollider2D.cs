using Talesmith.Authoring;

namespace Talesmith.Physics;

/// <summary>Configures the collision built for the tile map of the same entity.</summary>
/// <remarks>
/// Every entity with a <see cref="Runtime.Components.TileMapComponent"/> collides through the tile layers whose role is
/// <see cref="Assets.Maps.LayerRole.Collision"/>, with default settings when this component is absent. Non-empty cells are solid, or
/// shaped by their tile's collision polygons; tiles or layers with the bool property <c>oneWay</c> become platforms that are solid
/// only from above.
/// </remarks>
[Component("Tile Map Collider 2D", Category = "Physics", Icon = "grid-2x2", Description = "Collision settings of the entity's tile map.")]
public struct TileMapCollider2D
{
    [Tooltip("Turns the tile map's collision on or off.")]
    public bool Enabled = true;

    [Range(0, 1, Step = 0.05)]
    public float Friction = 0.4f;

    [Range(0, 1, Step = 0.05)]
    public float Restitution;

    [Range(0, PhysicsLayers.Count - 1, Step = 1)]
    [Layer]
    public int Layer;

    [Tooltip("Bit mask of the layers the tiles collide with; -1 collides with every layer.")]
    [LayerMask]
    public int CollisionMask = PhysicsLayers.All;

    public TileMapCollider2D()
    {
    }
}
