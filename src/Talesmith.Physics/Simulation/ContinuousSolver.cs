using System.Numerics;
using Talesmith.Physics.Broadphase;
using Talesmith.Physics.Collision;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Simulation;

/// <summary>Stops fast bodies at the first shape they would pass through during a step, by sweeping their shapes along the step's motion.</summary>
/// <remarks>
/// Every dynamic body that moves more than half its smallest extent in a step is swept against static shapes, so falling bodies never
/// tunnel through floors. Bodies with <see cref="CollisionDetection.Continuous"/> are swept every step against kinematic and dynamic
/// bodies too. Rotation during the step is ignored by the sweep.
/// </remarks>
internal sealed class ContinuousSolver(PhysicsState state)
{
    public void Solve()
    {
        var bodies = state.Bodies;
        for (var i = 0; i < state.BodyHighWater; i++)
        {
            ref var body = ref bodies[i];
            if (!body.IsAlive || body.Kind != BodyKind.Dynamic || !body.IsAwake || body.FirstFixture == PhysicsState.Null)
                continue;
            var motion = body.Center - body.Center0;
            var bullet = body.Has(BodyFlags.Bullet);
            var travel = motion.Length() + MathF.Abs(body.Angle - body.Angle0) * body.MaxExtent;
            if (travel == 0 || (!bullet && travel < 0.5f * body.MinExtent))
                continue;
            Sweep(i, motion, bullet);
        }
    }

    private void Sweep(int bodyId, Vector2 motion, bool bullet)
    {
        ref var body = ref state.Bodies[bodyId];
        var best = 1f;
        var slop = state.Settings.LinearSlop;
        for (var f = body.FirstFixture; f != PhysicsState.Null; f = state.Fixtures[f].Next)
        {
            ref var fixture = ref state.Fixtures[f];
            if (fixture.IsTrigger)
                continue;
            var start = fixture.Local.Transform(new Xf(body.Origin - motion, body.Q));
            var bounds = start.ComputeAabb().Sweep(motion);
            var query = new SweepQuery(state, bodyId, f, start, motion, bullet, best, slop);
            state.Broadphase.Query(bounds, ref query);
            best = query.Best;
        }

        if (best >= 1)
            return;
        body.Center = body.Center0 + best * motion;
        body.SyncOriginFromCenter();
    }

    private struct SweepQuery(PhysicsState state, int bodyId, int fixtureId, Shape start, Vector2 motion, bool bullet, float best, float slop) : IProxyQuery
    {
        public float Best = best;

        public bool Report(int proxy, int userData)
        {
            ref var other = ref state.Fixtures[userData];
            if (other.Body == bodyId || other.IsTrigger)
                return true;
            ref var otherBody = ref state.Bodies[other.Body];
            if (!bullet && otherBody.Kind != BodyKind.Static)
                return true;
            if (otherBody.Kind == BodyKind.Dynamic && otherBody.Has(BodyFlags.Bullet))
                return true;
            if (!state.ShouldCollide(fixtureId, userData))
                return true;

            var cast = Distance.Cast(other.World, start, motion, Best, slop, slop);
            if (cast.StartedInside)
            {
                // Already touching at the start: sweep a small core around the center instead, so resting bodies are not frozen.
                var core = Shape.Circle(start.Centroid, 0.25f * state.Bodies[bodyId].MinExtent);
                cast = Distance.Cast(other.World, core, motion, Best, slop, slop);
                if (cast.StartedInside)
                    return true;
            }

            if (!cast.Hit || cast.Fraction <= 0 || cast.Fraction >= Best)
                return true;
            if (other.OneWay && Vector2.Dot(cast.Normal, otherBody.Q.Rotate(other.LocalUp)) <= 0.5f)
                return true;
            Best = cast.Fraction;
            return true;
        }
    }
}
