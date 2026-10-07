using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Talesmith.Ecs;
using Talesmith.Physics.Geometry;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Simulation;

/// <summary>Mirrors entities with colliders, rigid bodies and character controllers into bodies, and writes the results back.</summary>
/// <remarks>
/// Components stay the source of truth: an edited collider is rebuilt, a transform moved by game code teleports its body and a velocity
/// set by game code replaces the simulated one. Changes are detected by comparing with what was last written, so nothing is copied for
/// bodies that did not change.
/// </remarks>
internal sealed class BodySynchronizer(PhysicsState state, ILogger logger)
{
    private static readonly QueryDescription Bodies = QueryDescription.With<Transform>().WithAny<Collider2D>().WithAny<Rigidbody2D>().WithAny<CharacterController2D>();

    private readonly List<Shape> _shapes = [];
    private int _stamp;

    /// <summary>Creates, updates and removes bodies to match the entities.</summary>
    public void Sync(World world)
    {
        _stamp++;
        foreach (var archetype in world.Query(Bodies))
        {
            var entities = archetype.Entities;
            var transforms = archetype.GetSpan<Transform>();
            var hasCollider = archetype.Has<Collider2D>();
            var hasRigidbody = archetype.Has<Rigidbody2D>();
            var hasCharacter = archetype.Has<CharacterController2D>();
            var colliders = hasCollider ? archetype.GetSpan<Collider2D>() : default;
            var rigidbodies = hasRigidbody ? archetype.GetSpan<Rigidbody2D>() : default;
            var characters = hasCharacter ? archetype.GetSpan<CharacterController2D>() : default;
            for (var i = 0; i < entities.Length; i++)
            {
                SyncEntity(entities[i], ref transforms[i],
                    ref hasCollider ? ref colliders[i] : ref Unsafe.NullRef<Collider2D>(),
                    ref hasRigidbody ? ref rigidbodies[i] : ref Unsafe.NullRef<Rigidbody2D>(),
                    ref hasCharacter ? ref characters[i] : ref Unsafe.NullRef<CharacterController2D>());
            }
        }

        for (var i = 0; i < state.BodyHighWater; i++)
        {
            ref var body = ref state.Bodies[i];
            if (body.IsAlive && !body.Has(BodyFlags.TileChunk) && body.SyncStamp != _stamp)
                state.DestroyBody(i);
        }
    }

    /// <summary>Creates the body of a single entity now, so forces and moves work before its first fixed step.</summary>
    /// <returns>The body, or <see cref="PhysicsState.Null"/> when the entity has no physics components.</returns>
    public int Ensure(World world, Entity entity)
    {
        var body = state.BodyOf(entity);
        if (body != PhysicsState.Null || !world.IsAlive(entity))
            return body;
        ref var transform = ref world.TryGetRef<Transform>(entity, out var hasTransform);
        if (!hasTransform)
            return PhysicsState.Null;
        ref var collider = ref world.TryGetRef<Collider2D>(entity, out var hasCollider);
        ref var rigidbody = ref world.TryGetRef<Rigidbody2D>(entity, out var hasRigidbody);
        ref var character = ref world.TryGetRef<CharacterController2D>(entity, out var hasCharacter);
        if (!hasCollider && !hasRigidbody && !hasCharacter)
            return PhysicsState.Null;
        SyncEntity(entity, ref transform, ref collider, ref rigidbody, ref character);
        return state.BodyOf(entity);
    }

    /// <summary>Writes simulated poses and velocities to the entities whose bodies changed.</summary>
    public void WriteBack(World world)
    {
        foreach (var archetype in world.Query(Bodies))
        {
            var entities = archetype.Entities;
            var transforms = archetype.GetSpan<Transform>();
            var hasRigidbody = archetype.Has<Rigidbody2D>();
            var rigidbodies = hasRigidbody ? archetype.GetSpan<Rigidbody2D>() : default;
            for (var i = 0; i < entities.Length; i++)
            {
                var id = state.BodyOf(entities[i]);
                if (id == PhysicsState.Null)
                    continue;
                ref var body = ref state.Bodies[id];
                if (body.Kind == BodyKind.Static || body.Has(BodyFlags.Character))
                    continue;
                ref var transform = ref transforms[i];
                if (body.Origin != body.WrittenPosition || body.Angle != body.WrittenRotation)
                {
                    transform.Position = body.Origin;
                    transform.Rotation = body.Angle;
                    body.WrittenPosition = body.Origin;
                    body.WrittenRotation = body.Angle;
                }

                if (hasRigidbody && body.Kind == BodyKind.Dynamic && (body.Velocity != body.WrittenVelocity || body.AngularVelocity != body.WrittenAngularVelocity))
                {
                    ref var rigidbody = ref rigidbodies[i];
                    rigidbody.Velocity = body.Velocity;
                    rigidbody.AngularVelocity = body.AngularVelocity;
                    body.WrittenVelocity = body.Velocity;
                    body.WrittenAngularVelocity = body.AngularVelocity;
                }
            }
        }
    }

    /// <summary>Puts interpolated entities back at their simulated pose and remembers every pose before the step moves it.</summary>
    public void BeginStep(World world)
    {
        for (var i = 0; i < state.BodyHighWater; i++)
        {
            ref var body = ref state.Bodies[i];
            if (!body.IsAlive || body.Kind == BodyKind.Static)
                continue;
            if (body.Has(BodyFlags.Interpolate) && world.IsAlive(body.Entity))
            {
                ref var transform = ref world.TryGetRef<Transform>(body.Entity, out var exists);
                if (exists && transform.Position == body.WrittenPosition && transform.Rotation == body.WrittenRotation)
                {
                    transform.Position = body.Origin;
                    transform.Rotation = body.Angle;
                    body.WrittenPosition = body.Origin;
                    body.WrittenRotation = body.Angle;
                }
            }

            body.PreviousOrigin = body.Origin;
            body.PreviousAngle = body.Angle;
        }
    }

    /// <summary>Shows interpolated entities between their last two simulated poses.</summary>
    public void Interpolate(World world, float alpha)
    {
        for (var i = 0; i < state.BodyHighWater; i++)
        {
            ref var body = ref state.Bodies[i];
            if (!body.IsAlive || !body.Has(BodyFlags.Interpolate) || !world.IsAlive(body.Entity))
                continue;
            ref var transform = ref world.TryGetRef<Transform>(body.Entity, out var exists);
            if (!exists || transform.Position != body.WrittenPosition || transform.Rotation != body.WrittenRotation)
                continue;
            transform.Position = Vector2.Lerp(body.PreviousOrigin, body.Origin, alpha);
            transform.Rotation = body.PreviousAngle + (body.Angle - body.PreviousAngle) * alpha;
            body.WrittenPosition = transform.Position;
            body.WrittenRotation = transform.Rotation;
        }
    }

    private void SyncEntity(Entity entity, ref Transform transform, ref Collider2D collider, ref Rigidbody2D rigidbody, ref CharacterController2D character)
    {
        var hasCollider = !Unsafe.IsNullRef(ref collider);
        var hasRigidbody = !Unsafe.IsNullRef(ref rigidbody);
        var hasCharacter = !Unsafe.IsNullRef(ref character);
        var kind = hasRigidbody
            ? rigidbody.Type switch { BodyType.Dynamic => BodyKind.Dynamic, BodyType.Kinematic => BodyKind.Kinematic, _ => BodyKind.Static }
            : hasCharacter ? BodyKind.Kinematic : BodyKind.Static;
        if (hasCharacter && kind == BodyKind.Dynamic)
            kind = BodyKind.Kinematic;

        var flags = BodyFlags.CanSleep;
        if (hasRigidbody)
        {
            if (rigidbody.FixedRotation)
                flags |= BodyFlags.FixedRotation;
            if (rigidbody.CollisionDetection == CollisionDetection.Continuous)
                flags |= BodyFlags.Bullet;
            if (rigidbody.Interpolation == RigidbodyInterpolation.Interpolate)
                flags |= BodyFlags.Interpolate;
            if (!rigidbody.CanSleep)
                flags &= ~BodyFlags.CanSleep;
        }

        if (hasCharacter)
        {
            flags |= BodyFlags.Character | BodyFlags.FixedRotation;
            if (character.Interpolate)
                flags |= BodyFlags.Interpolate;
        }

        var signature = hasCollider ? ColliderSignature.From(collider, transform.Scale) : default;
        var mass = hasRigidbody ? rigidbody.Mass : 1;
        var autoMass = hasRigidbody && rigidbody.AutoMass;

        var id = state.BodyOf(entity);
        if (id != PhysicsState.Null && (state.Bodies[id].Kind != kind || state.Bodies[id].Has(BodyFlags.Character) != hasCharacter))
        {
            state.DestroyBody(id);
            id = PhysicsState.Null;
        }

        if (id == PhysicsState.Null)
        {
            id = state.CreateBody(entity, kind, flags, transform.Position, transform.Rotation);
            ref var created = ref state.Bodies[id];
            created.WrittenPosition = transform.Position;
            created.WrittenRotation = transform.Rotation;
            created.Collider = signature;
            if (hasCollider)
                BuildFixtures(id, collider, transform.Scale);
            state.UpdateMass(id, mass, autoMass);
            if (hasRigidbody && kind != BodyKind.Static)
            {
                created.Velocity = rigidbody.Velocity;
                created.AngularVelocity = created.InvInertia == 0 && kind == BodyKind.Dynamic ? 0 : rigidbody.AngularVelocity;
                created.WrittenVelocity = rigidbody.Velocity;
                created.WrittenAngularVelocity = rigidbody.AngularVelocity;
            }
        }

        ref var body = ref state.Bodies[id];
        body.SyncStamp = _stamp;
        var rebuildMass = false;
        const BodyFlags configurable = BodyFlags.FixedRotation | BodyFlags.Bullet | BodyFlags.Interpolate | BodyFlags.CanSleep;
        if ((body.Flags & configurable) != (flags & configurable))
        {
            rebuildMass = ((body.Flags ^ flags) & BodyFlags.FixedRotation) != 0;
            body.Flags = (body.Flags & ~configurable) | (flags & configurable);
        }

        if (!signature.Equals(body.Collider))
        {
            while (body.FirstFixture != PhysicsState.Null)
                state.DestroyFixture(body.FirstFixture);
            body.Collider = signature;
            if (hasCollider)
                BuildFixtures(id, collider, transform.Scale);
            rebuildMass = true;
            state.WakeBody(id);
        }

        if (rebuildMass || mass != body.RigidbodyMass || autoMass != body.RigidbodyAutoMass)
            state.UpdateMass(id, mass, autoMass);

        if (hasRigidbody)
        {
            body.GravityScale = rigidbody.GravityScale;
            body.LinearDrag = rigidbody.LinearDrag;
            body.AngularDrag = rigidbody.AngularDrag;
        }

        if (transform.Position != body.WrittenPosition || transform.Rotation != body.WrittenRotation)
        {
            var displacement = transform.Position - body.Origin;
            body.SetPose(transform.Position, transform.Rotation);
            body.PreviousOrigin = body.Origin;
            body.PreviousAngle = body.Angle;
            body.WrittenPosition = transform.Position;
            body.WrittenRotation = transform.Rotation;
            body.Flags |= BodyFlags.Moved;
            state.WakeBody(id);
            state.SynchronizeFixtures(id, kind == BodyKind.Static ? Vector2.Zero : displacement);
        }

        if (!hasRigidbody || kind == BodyKind.Static)
            return;
        if (kind == BodyKind.Kinematic)
        {
            if (!hasCharacter)
            {
                body.Velocity = rigidbody.Velocity;
                body.AngularVelocity = rigidbody.AngularVelocity;
            }

            return;
        }

        if (rigidbody.Velocity != body.WrittenVelocity || rigidbody.AngularVelocity != body.WrittenAngularVelocity)
        {
            body.Velocity = rigidbody.Velocity;
            body.AngularVelocity = body.InvInertia == 0 ? 0 : rigidbody.AngularVelocity;
            body.WrittenVelocity = rigidbody.Velocity;
            body.WrittenAngularVelocity = rigidbody.AngularVelocity;
            state.WakeBody(id);
        }
    }

    private void BuildFixtures(int bodyId, in Collider2D collider, Vector2 scale)
    {
        _shapes.Clear();
        try
        {
            ColliderShapes.Build(collider, scale, _shapes);
        }
        catch (ArgumentException ex)
        {
            logger.InvalidCollider(state.Bodies[bodyId].Entity.ToString(), ex.Message);
            return;
        }

        var options = new FixtureOptions(collider.Friction, collider.Restitution, collider.Density, collider.Layer & 31, collider.CollisionMask,
            collider.IsTrigger, collider.OneWay, Rot.FromAngle(collider.Rotation).Rotate(new Vector2(0, -1)));
        foreach (var shape in _shapes)
            state.CreateFixture(bodyId, shape, options);
    }
}
