using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.Physics;

/// <summary>A touch between two entities. Shapes of a tile map report the map's entity.</summary>
/// <param name="Point">A world point where the shapes touch.</param>
/// <param name="Normal">The direction from <paramref name="A"/> toward <paramref name="B"/> at the contact.</param>
/// <param name="NormalImpulse">The impulse that pushed the entities apart during the step; 0 for triggers.</param>
/// <param name="RelativeVelocity">The velocity of <paramref name="B"/> relative to <paramref name="A"/> at the point when the contact was evaluated.</param>
/// <param name="IsTrigger">Whether either collider is a trigger, so the entities overlap instead of colliding.</param>
public readonly record struct PhysicsContact(Entity A, Entity B, Vector2 Point, Vector2 Normal, float NormalImpulse, Vector2 RelativeVelocity, bool IsTrigger)
{
    public bool Involves(Entity entity) => A == entity || B == entity;

    /// <summary>The contact as seen from <paramref name="self"/>, which must be <see cref="A"/> or <see cref="B"/>.</summary>
    public ContactInfo For(Entity self) => self == A
        ? new ContactInfo(A, B, Point, -Normal, NormalImpulse, RelativeVelocity, IsTrigger)
        : new ContactInfo(B, A, Point, Normal, NormalImpulse, -RelativeVelocity, IsTrigger);
}

/// <summary>A touch seen from one of the two entities.</summary>
/// <param name="Normal">Points from <paramref name="Other"/> toward <paramref name="Self"/>; for a character standing on the ground it points up.</param>
/// <param name="RelativeVelocity">The velocity of <paramref name="Other"/> relative to <paramref name="Self"/>.</param>
public readonly record struct ContactInfo(Entity Self, Entity Other, Vector2 Point, Vector2 Normal, float NormalImpulse, Vector2 RelativeVelocity, bool IsTrigger);

/// <summary>Raised when two colliders start touching.</summary>
public readonly record struct CollisionEntered(PhysicsContact Contact);

/// <summary>Raised every fixed step while two colliders keep touching and at least one of them is awake.</summary>
public readonly record struct CollisionStayed(PhysicsContact Contact);

/// <summary>Raised when two colliders stop touching, including when one is removed or destroyed.</summary>
public readonly record struct CollisionExited(PhysicsContact Contact);

/// <summary>Raised when a collider starts overlapping a trigger collider.</summary>
/// <remarks>Unrelated to the runtime's <c>TriggerEntered</c>, which reports map trigger areas entered by a trigger activator.</remarks>
public readonly record struct PhysicsTriggerEntered(PhysicsContact Contact);

/// <summary>Raised every fixed step while a collider keeps overlapping a trigger and either is awake.</summary>
public readonly record struct PhysicsTriggerStayed(PhysicsContact Contact);

/// <summary>Raised when a collider stops overlapping a trigger, including when one is removed or destroyed.</summary>
public readonly record struct PhysicsTriggerExited(PhysicsContact Contact);

/// <summary>Receives the contacts of entities after each fixed step, seen from the entity concerned.</summary>
/// <remarks>Implement only the methods needed; register with <see cref="IPhysicsWorld.AddCollisionListener(ICollisionListener)"/>.</remarks>
public interface ICollisionListener
{
    void OnCollisionEntered(in ContactInfo contact)
    {
    }

    void OnCollisionStayed(in ContactInfo contact)
    {
    }

    void OnCollisionExited(in ContactInfo contact)
    {
    }

    void OnTriggerEntered(in ContactInfo contact)
    {
    }

    void OnTriggerStayed(in ContactInfo contact)
    {
    }

    void OnTriggerExited(in ContactInfo contact)
    {
    }
}
