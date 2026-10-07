using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Grids;

namespace Talesmith.Physics;

/// <summary>The physics simulation of one scene: queries, forces, character movement and contacts.</summary>
/// <remarks>
/// Each scene has its own world, built from the scene's <see cref="Collider2D"/>, <see cref="Rigidbody2D"/>,
/// <see cref="CharacterController2D"/> and tile map entities and simulated in the fixed update while the game plays. Queries see the
/// colliders as of the last fixed step; tile map collision is built on demand where queries look. Queries with boxes, circles, capsules and
/// convex polygons do not allocate.
/// </remarks>
public interface IPhysicsWorld
{
    PhysicsSettings Settings { get; }

    /// <summary>The number of simulated bodies, including one per tile map chunk with collision.</summary>
    int BodyCount { get; }

    /// <summary>The number of pairs of shapes close enough to be checked precisely.</summary>
    int ContactCount { get; }

    /// <summary>Finds the closest collider along a ray.</summary>
    /// <param name="direction">The ray's direction; it does not need to be normalized.</param>
    /// <returns>Whether anything was hit. Rays starting inside a collider do not hit it.</returns>
    bool RayCast(Vector2 origin, Vector2 direction, float maxDistance, QueryFilter filter, out RaycastHit hit);

    /// <summary>Finds every collider along a ray, nearest first.</summary>
    /// <returns>The number of hits written to <paramref name="results"/>; when it fills up, the nearest hits are kept.</returns>
    int RayCastAll(Vector2 origin, Vector2 direction, float maxDistance, QueryFilter filter, Span<RaycastHit> results);

    /// <summary>Sweeps a shape along a straight line and finds the first collider it would touch.</summary>
    /// <param name="rotation">The shape's rotation in radians.</param>
    bool ShapeCast(QueryShape shape, Vector2 position, float rotation, Vector2 direction, float maxDistance, QueryFilter filter, out RaycastHit hit);

    /// <summary>Finds the entities whose colliders contain a point.</summary>
    /// <returns>The number of entities written to <paramref name="results"/>, each once.</returns>
    int OverlapPoint(Vector2 point, QueryFilter filter, Span<Entity> results);

    /// <summary>Finds the entities whose colliders overlap a shape placed at <paramref name="position"/>.</summary>
    /// <returns>The number of entities written to <paramref name="results"/>, each once.</returns>
    int OverlapShape(QueryShape shape, Vector2 position, float rotation, QueryFilter filter, Span<Entity> results);

    /// <summary>Finds the tile map cell a hit landed in, when it hit a tile map.</summary>
    bool TryGetHitCell(in RaycastHit hit, out GridCoord cell);

    /// <summary>Pushes a dynamic body during the next fixed step, in world units of mass times acceleration.</summary>
    void AddForce(Entity entity, Vector2 force);

    /// <summary>Pushes a dynamic body at a world point, which also turns it.</summary>
    void AddForceAtPosition(Entity entity, Vector2 force, Vector2 point);

    void AddTorque(Entity entity, float torque);

    /// <summary>Changes a dynamic body's velocity at once by <paramref name="impulse"/> divided by its mass.</summary>
    void AddImpulse(Entity entity, Vector2 impulse);

    /// <summary>Applies an impulse at a world point, changing both linear and angular velocity at once.</summary>
    void AddImpulseAtPosition(Entity entity, Vector2 impulse, Vector2 point);

    void AddAngularImpulse(Entity entity, float impulse);

    /// <summary>Moves a kinematic body to a position during the next fixed step, pushing what is in the way.</summary>
    void MovePosition(Entity entity, Vector2 position);

    /// <summary>Turns a kinematic body to an angle during the next fixed step.</summary>
    void MoveRotation(Entity entity, float rotation);

    /// <summary>Wakes a sleeping body and every body resting on it.</summary>
    void WakeUp(Entity entity);

    /// <summary>Whether the entity's body is simulating; static colliders and unknown entities are never awake.</summary>
    bool IsAwake(Entity entity);

    /// <summary>Moves a character by sliding along what it touches; see <see cref="CharacterController2D"/>.</summary>
    /// <param name="motion">The distance to move this step, such as velocity times the fixed delta time.</param>
    /// <returns>The sides on which the character touched something; also stored in <see cref="CharacterController2D.Collisions"/>.</returns>
    CharacterCollisions MoveCharacter(Entity entity, Vector2 motion);

    /// <summary>Lists what an entity touches now, seen from the entity.</summary>
    /// <returns>The number of contacts written to <paramref name="results"/>.</returns>
    int GetContacts(Entity entity, Span<ContactInfo> results);

    /// <summary>Calls <paramref name="listener"/> for the contacts of every entity, once from each side; dispose the result to stop.</summary>
    IDisposable AddCollisionListener(ICollisionListener listener);

    /// <summary>Calls <paramref name="listener"/> for the contacts of one entity; dispose the result to stop.</summary>
    IDisposable AddCollisionListener(Entity entity, ICollisionListener listener);
}

/// <summary>Shorter overloads of <see cref="IPhysicsWorld"/> queries that use <see cref="QueryFilter.Default"/>.</summary>
public static class PhysicsWorldExtensions
{
    public static bool RayCast(this IPhysicsWorld physics, Vector2 origin, Vector2 direction, float maxDistance, out RaycastHit hit) =>
        physics.RayCast(origin, direction, maxDistance, QueryFilter.Default, out hit);

    public static int RayCastAll(this IPhysicsWorld physics, Vector2 origin, Vector2 direction, float maxDistance, Span<RaycastHit> results) =>
        physics.RayCastAll(origin, direction, maxDistance, QueryFilter.Default, results);

    public static int OverlapPoint(this IPhysicsWorld physics, Vector2 point, Span<Entity> results) =>
        physics.OverlapPoint(point, QueryFilter.Default, results);

    public static int OverlapCircle(this IPhysicsWorld physics, Vector2 center, float radius, QueryFilter filter, Span<Entity> results) =>
        physics.OverlapShape(QueryShape.Circle(radius), center, 0, filter, results);

    public static int OverlapBox(this IPhysicsWorld physics, Vector2 center, Vector2 size, float rotation, QueryFilter filter, Span<Entity> results) =>
        physics.OverlapShape(QueryShape.Box(size), center, rotation, filter, results);
}
