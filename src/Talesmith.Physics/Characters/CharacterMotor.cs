using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Physics.Broadphase;
using Talesmith.Physics.Collision;
using Talesmith.Physics.Geometry;
using Talesmith.Physics.Simulation;
using Talesmith.Physics.Tiles;
using Talesmith.Runtime.Components;

namespace Talesmith.Physics.Characters;

/// <summary>Moves character bodies by sweeping their shapes and sliding along what they hit.</summary>
/// <remarks>
/// A move first pushes the character out of anything that moved into it, then sweeps it along the motion up to
/// <see cref="MaxSlides"/> times, each time removing the part of the motion that points into the surface hit. Ground is a surface whose
/// normal is within the slope limit of up; steeper surfaces are walls, which the character neither climbs nor slides up. Dynamic bodies
/// are obstacles like any other shape, and one-way shapes block only when landed on from above.
/// </remarks>
internal sealed class CharacterMotor(PhysicsState state, TileColliderCache tiles)
{
    private const int MaxSlides = 4;
    private const int MaxDepenetrations = 4;

    public CharacterCollisions Move(int bodyId, ref CharacterController2D controller, ref Transform transform, Vector2 motion)
    {
        var up = Vec.Normalize(controller.Up);
        if (up == Vector2.Zero)
            up = new Vector2(0, -1);
        var context = new MoveContext(bodyId, up, MathF.Cos(Math.Clamp(controller.SlopeLimit, 0, MathF.PI / 2)),
            MathF.Max(controller.SkinWidth, 0.01f), 0.5f * MathF.Min(controller.SkinWidth, state.Settings.LinearSlop));
        var start = state.Bodies[bodyId].Origin;
        var position = start;
        var wasGrounded = controller.IsGrounded;
        var result = new MoveResult();

        Depenetrate(context, ref position);

        var remaining = motion;
        for (var slide = 0; slide < MaxSlides && remaining.LengthSquared() > 1e-10f; slide++)
        {
            var hit = Cast(context, position, remaining);
            if (!hit.Hit)
            {
                position += remaining;
                break;
            }

            position += remaining * hit.Fraction;
            if (hit.Normal == Vector2.Zero)
                break;
            var side = Classify(context, hit.Normal);
            result.Record(side, hit);
            var rest = remaining * (1 - hit.Fraction);
            if (side == CharacterCollisions.Sides && controller.StepOffset > 0 && (wasGrounded || result.Grounded) && Vector2.Dot(rest, up) <= 0
                && TryStep(context, controller.StepOffset, ref position, rest, ref result))
                break;

            var normal = hit.Normal;
            var along = Vector2.Dot(normal, up);
            if (side == CharacterCollisions.Sides && along > 0)
            {
                var flat = Vec.Normalize(normal - up * along);
                if (flat != Vector2.Zero)
                    normal = flat;
            }

            var into = Vector2.Dot(rest, normal);
            if (into < 0)
                rest -= normal * into;
            remaining = rest;
        }

        if (wasGrounded && !result.Grounded && controller.GroundSnapDistance > 0 && Vector2.Dot(motion, up) <= 1e-4f)
        {
            var snap = Cast(context, position, -up * controller.GroundSnapDistance);
            if (snap.Hit && snap.Normal != Vector2.Zero && Classify(context, snap.Normal) == CharacterCollisions.Below)
            {
                position += -up * controller.GroundSnapDistance * snap.Fraction;
                result.Record(CharacterCollisions.Below, snap);
            }
        }

        if (!result.Grounded)
        {
            var probe = Cast(context, position, -up * 2 * context.Skin);
            if (probe.Hit && probe.Normal != Vector2.Zero && Classify(context, probe.Normal) == CharacterCollisions.Below)
                result.Record(CharacterCollisions.Below, probe);
        }

        var displacement = position - start;
        // Casts may have built tile chunks, which can grow the body array, so the reference is taken only now.
        ref var body = ref state.Bodies[bodyId];
        body.SetPose(position, body.Angle);
        body.Velocity = displacement / state.LastTimeStep;
        body.Flags |= BodyFlags.Moved;
        state.SynchronizeFixtures(bodyId, displacement);
        transform.Position = position;
        body.WrittenPosition = position;
        body.WrittenRotation = transform.Rotation;

        controller.Collisions = result.Collisions;
        controller.GroundNormal = result.GroundNormal;
        controller.Ground = result.Ground;
        controller.LastMotion = displacement;
        return result.Collisions;
    }

    private static CharacterCollisions Classify(in MoveContext context, Vector2 normal)
    {
        var along = Vector2.Dot(normal, context.Up);
        if (along >= context.CosSlope)
            return CharacterCollisions.Below;
        return along <= -context.CosSlope ? CharacterCollisions.Above : CharacterCollisions.Sides;
    }

    private bool TryStep(in MoveContext context, float stepOffset, ref Vector2 position, Vector2 rest, ref MoveResult result)
    {
        var up = context.Up;
        var forward = rest - up * Vector2.Dot(rest, up);
        if (forward.LengthSquared() < 1e-8f)
            return false;

        var rise = Cast(context, position, up * stepOffset);
        var height = stepOffset * (rise.Hit ? rise.Fraction : 1);
        if (height <= state.Settings.LinearSlop)
            return false;
        var raised = position + up * height;
        var ahead = Cast(context, raised, forward);
        var advanced = raised + forward * (ahead.Hit ? ahead.Fraction : 1);
        if (Vector2.DistanceSquared(advanced, raised) < 1e-6f)
            return false;
        var drop = Cast(context, advanced, -up * (height + context.Skin));
        if (!drop.Hit || drop.Normal == Vector2.Zero || Classify(context, drop.Normal) != CharacterCollisions.Below)
            return false;

        position = advanced - up * (height + context.Skin) * drop.Fraction;
        result.Record(CharacterCollisions.Below, drop);
        return true;
    }

    private void Depenetrate(in MoveContext context, ref Vector2 position)
    {
        for (var iteration = 0; iteration < MaxDepenetrations; iteration++)
        {
            var query = new PenetrationQuery(state, context);
            for (var f = state.Bodies[context.Body].FirstFixture; f != PhysicsState.Null; f = state.Fixtures[f].Next)
            {
                ref var fixture = ref state.Fixtures[f];
                if (fixture.IsTrigger)
                    continue;
                query.Fixture = f;
                query.Shape = Placed(fixture, position);
                var bounds = query.Shape.ComputeAabb().Inflate(context.Skin);
                tiles.EnsureRegion(bounds);
                state.Broadphase.Query(bounds, ref query);
            }

            if (query.Depth <= 0)
                return;
            position += query.Normal * (query.Depth + context.Skin);
        }
    }

    private CastHit Cast(in MoveContext context, Vector2 position, Vector2 translation)
    {
        var query = new SweepQuery(state, context, translation);
        for (var f = state.Bodies[context.Body].FirstFixture; f != PhysicsState.Null; f = state.Fixtures[f].Next)
        {
            ref var fixture = ref state.Fixtures[f];
            if (fixture.IsTrigger)
                continue;
            query.Fixture = f;
            query.Shape = Placed(fixture, position);
            var bounds = query.Shape.ComputeAabb().Sweep(translation).Inflate(context.Skin);
            tiles.EnsureRegion(bounds);
            state.Broadphase.Query(bounds, ref query);
        }

        return query.Best;
    }

    private Shape Placed(in Fixture fixture, Vector2 position) => fixture.Local.Transform(new Xf(position, state.Bodies[fixture.Body].Q));

    private static bool IsObstacle(PhysicsState state, in MoveContext context, int fixtureId, int otherId)
    {
        ref var other = ref state.Fixtures[otherId];
        if (other.Body == context.Body || other.IsTrigger)
            return false;
        ref var own = ref state.Fixtures[fixtureId];
        return PhysicsLayers.Contains(own.Mask, other.Layer) && PhysicsLayers.Contains(other.Mask, own.Layer)
            && state.Settings.ShouldLayersCollide(own.Layer, other.Layer);
    }

    private readonly record struct MoveContext(int Body, Vector2 Up, float CosSlope, float Skin, float MinimumGap);

    private struct CastHit
    {
        public bool Hit;
        public float Fraction;
        public Vector2 Normal;
        public Entity Entity;
    }

    private struct MoveResult
    {
        public CharacterCollisions Collisions;
        public Vector2 GroundNormal;
        public Entity Ground;

        public readonly bool Grounded => (Collisions & CharacterCollisions.Below) != 0;

        public void Record(CharacterCollisions side, in CastHit hit)
        {
            Collisions |= side;
            if (side != CharacterCollisions.Below)
                return;
            GroundNormal = hit.Normal;
            Ground = hit.Entity;
        }
    }

    private struct SweepQuery(PhysicsState state, MoveContext context, Vector2 translation) : IProxyQuery
    {
        public int Fixture;
        public Shape Shape;
        public CastHit Best = new() { Fraction = 1 };

        public bool Report(int proxy, int userData)
        {
            if (!IsObstacle(state, context, Fixture, userData))
                return true;
            ref var other = ref state.Fixtures[userData];
            var cast = Distance.Cast(other.World, Shape, translation, Best.Fraction, context.Skin, context.MinimumGap);
            if (!cast.Hit || (Best.Hit && cast.Fraction >= Best.Fraction))
                return true;
            if (other.OneWay)
            {
                var platformUp = state.Bodies[other.Body].Q.Rotate(other.LocalUp);
                if (cast.Normal == Vector2.Zero || Vector2.Dot(translation, platformUp) >= 0 || Vector2.Dot(cast.Normal, platformUp) <= 0.5f)
                    return true;
            }

            Best = new CastHit { Hit = true, Fraction = cast.Fraction, Normal = cast.Normal, Entity = state.Bodies[other.Body].Entity };
            return true;
        }
    }

    private struct PenetrationQuery(PhysicsState state, MoveContext context) : IProxyQuery
    {
        public int Fixture;
        public Shape Shape;
        public float Depth;
        public Vector2 Normal;

        public bool Report(int proxy, int userData)
        {
            if (!IsObstacle(state, context, Fixture, userData))
                return true;
            ref var other = ref state.Fixtures[userData];
            if (other.OneWay)
                return true;
            Collide.Shapes(other.World, Shape, 0, state.Settings.LinearSlop, out var manifold);
            if (manifold.PointCount == 0)
                return true;
            var depth = -manifold.MinSeparation;
            if (depth > Depth)
            {
                Depth = depth;
                Normal = manifold.Normal;
            }

            return true;
        }
    }
}
