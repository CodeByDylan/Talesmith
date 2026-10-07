using System.Numerics;
using System.Runtime.CompilerServices;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Simulation;

/// <summary>Resolves contacts with sequential impulses over several substeps, with soft position correction.</summary>
/// <remarks>
/// Each fixed step is split into <see cref="PhysicsSettings.Substeps"/>. A substep integrates velocities, warm starts with the impulses
/// carried over from before, solves contacts with a soft spring that pushes overlapping shapes apart, integrates positions and then
/// relaxes the contacts without that spring so the push does not turn into bounce. Restitution is applied once at the end of the step to
/// contacts that collided fast enough. Contact separation is tracked through the bodies' motion during the step, so shapes are not
/// collided again between substeps. The substeps work on compact copies of the moving state of each body, which keeps them cache-friendly.
/// </remarks>
internal sealed class ContactSolver(PhysicsState state)
{
    private const float MaxRotationPerStep = 0.25f * MathF.PI;

    private Constraint[] _constraints = new Constraint[64];
    private int _count;
    private SolverBody[] _bodies = new SolverBody[64];
    private Mover[] _movers = new Mover[64];
    private int _moverCount;

    public void Solve(float dt)
    {
        var settings = state.Settings;
        var substeps = Math.Max(1, settings.Substeps);
        var h = dt / substeps;
        var inverseH = 1 / h;
        var contactHertz = MathF.Min(settings.ContactHertz, 0.25f * substeps / dt);
        var soft = Softness.Make(contactHertz, settings.ContactDampingRatio, h);
        var staticSoft = Softness.Make(2 * contactHertz, settings.ContactDampingRatio, h);
        var maxPush = settings.ContactPushMaxVelocity;
        var maxTranslation = settings.MaxTranslationPerStep / substeps;

        BeginStep(dt, h, settings.Gravity);
        Prepare(settings.WarmStarting, soft, staticSoft);
        for (var i = 0; i < substeps; i++)
        {
            IntegrateVelocities(h);
            WarmStart();
            for (var j = 0; j < Math.Max(1, settings.VelocityIterations); j++)
                SolveContacts(inverseH, maxPush, useBias: true);
            IntegratePositions(h, maxTranslation);
            for (var j = 0; j < settings.RelaxIterations; j++)
                SolveContacts(inverseH, maxPush, useBias: false);
            AccumulateImpulses();
        }

        ApplyRestitution(settings.RestitutionThreshold);
        StoreImpulses();
        Finish();
    }

    private void BeginStep(float dt, float h, Vector2 gravity)
    {
        var bodies = state.Bodies;
        var count = state.BodyHighWater;
        if (_bodies.Length < count)
            _bodies = new SolverBody[Math.Max(count, _bodies.Length * 2)];
        _moverCount = 0;
        for (var i = 0; i < count; i++)
        {
            ref var body = ref bodies[i];
            ref var solverBody = ref _bodies[i];
            solverBody = new SolverBody { DQ = Rot.Identity };
            if (!body.IsAlive)
                continue;
            body.Center0 = body.Center;
            body.Angle0 = body.Angle;
            if (body.Kind == BodyKind.Static)
                continue;
            if (body.Kind == BodyKind.Kinematic && body.Has(BodyFlags.HasMoveTarget))
            {
                body.Velocity = (body.MoveTarget - body.Origin) / dt;
                body.AngularVelocity = (body.MoveTargetAngle - body.Angle) / dt;
            }

            solverBody.V = body.Velocity;
            solverBody.W = body.AngularVelocity;
            if (!body.IsAwake || body.Has(BodyFlags.Character))
                continue;

            if (_moverCount == _movers.Length)
                Array.Resize(ref _movers, _movers.Length * 2);
            var dynamic = body.Kind == BodyKind.Dynamic;
            _movers[_moverCount++] = new Mover
            {
                Body = i,
                Dynamic = dynamic,
                Rotates = !dynamic || body.InvInertia != 0,
                Acceleration = dynamic ? body.GravityScale * gravity + body.InvMass * body.Force : Vector2.Zero,
                AngularAcceleration = dynamic ? body.InvInertia * body.Torque : 0,
                LinearDamping = dynamic ? 1 / (1 + h * body.LinearDrag) : 1,
                AngularDamping = dynamic ? 1 / (1 + h * body.AngularDrag) : 1
            };
        }
    }

    private void Prepare(bool warmStarting, Softness soft, Softness staticSoft)
    {
        _count = 0;
        var bodies = state.Bodies;
        for (var c = 0; c < state.ActiveContactCount; c++)
        {
            var id = state.ActiveContacts[c];
            ref var contact = ref state.Contacts[id];
            if (!contact.IsSolid)
                continue;
            ref var bodyA = ref bodies[contact.BodyA];
            ref var bodyB = ref bodies[contact.BodyB];
            var dynamicAwakeA = bodyA.Kind == BodyKind.Dynamic && bodyA.IsAwake;
            var dynamicAwakeB = bodyB.Kind == BodyKind.Dynamic && bodyB.IsAwake;
            if (!dynamicAwakeA && !dynamicAwakeB)
                continue;

            if (_count == _constraints.Length)
                Array.Resize(ref _constraints, _constraints.Length * 2);
            ref var constraint = ref _constraints[_count++];
            ref var manifold = ref contact.Manifold;
            var mA = dynamicAwakeA ? bodyA.InvMass : 0;
            var iA = dynamicAwakeA ? bodyA.InvInertia : 0;
            var mB = dynamicAwakeB ? bodyB.InvMass : 0;
            var iB = dynamicAwakeB ? bodyB.InvInertia : 0;
            constraint.Contact = id;
            constraint.BodyA = contact.BodyA;
            constraint.BodyB = contact.BodyB;
            constraint.Normal = manifold.Normal;
            constraint.Friction = contact.Friction;
            constraint.Restitution = contact.Restitution;
            constraint.PointCount = manifold.PointCount;
            constraint.InvMassA = mA;
            constraint.InvInertiaA = iA;
            constraint.InvMassB = mB;
            constraint.InvInertiaB = iB;
            constraint.Softness = mA == 0 || mB == 0 ? staticSoft : soft;
            var normal = manifold.Normal;
            var tangent = Vec.Cross(normal, 1f);
            ref var stateA = ref _bodies[contact.BodyA];
            ref var stateB = ref _bodies[contact.BodyB];

            for (var j = 0; j < manifold.PointCount; j++)
            {
                ref var mp = ref manifold.Points[j];
                ref var cp = ref constraint.Points[j];
                var rA = mp.Point - bodyA.Center;
                var rB = mp.Point - bodyB.Center;
                cp.RA = rA;
                cp.RB = rB;
                cp.AdjustedSeparation = mp.Separation - Vector2.Dot(rB - rA, normal);
                cp.NormalImpulse = warmStarting ? mp.NormalImpulse : 0;
                cp.TangentImpulse = warmStarting ? mp.TangentImpulse : 0;
                cp.MaxNormalImpulse = 0;
                cp.TotalNormalImpulse = 0;

                var rnA = Vec.Cross(rA, normal);
                var rnB = Vec.Cross(rB, normal);
                var kNormal = mA + mB + iA * rnA * rnA + iB * rnB * rnB;
                cp.NormalMass = kNormal > 0 ? 1 / kNormal : 0;

                var rtA = Vec.Cross(rA, tangent);
                var rtB = Vec.Cross(rB, tangent);
                var kTangent = mA + mB + iA * rtA * rtA + iB * rtB * rtB;
                cp.TangentMass = kTangent > 0 ? 1 / kTangent : 0;

                cp.RelativeVelocity = Vector2.Dot(normal, RelativeVelocity(stateA, stateB, rA, rB));
            }
        }
    }

    private void IntegrateVelocities(float h)
    {
        for (var i = 0; i < _moverCount; i++)
        {
            ref var mover = ref _movers[i];
            if (!mover.Dynamic)
                continue;
            ref var body = ref _bodies[mover.Body];
            body.V = (body.V + h * mover.Acceleration) * mover.LinearDamping;
            body.W = mover.Rotates ? (body.W + h * mover.AngularAcceleration) * mover.AngularDamping : 0;
        }
    }

    private void WarmStart()
    {
        for (var c = 0; c < _count; c++)
        {
            ref var constraint = ref _constraints[c];
            ref var bodyA = ref _bodies[constraint.BodyA];
            ref var bodyB = ref _bodies[constraint.BodyB];
            var normal = constraint.Normal;
            var tangent = Vec.Cross(normal, 1f);
            for (var j = 0; j < constraint.PointCount; j++)
            {
                ref var cp = ref constraint.Points[j];
                Apply(ref bodyA, ref bodyB, constraint, cp.RA, cp.RB, cp.NormalImpulse * normal + cp.TangentImpulse * tangent);
            }
        }
    }

    /// <param name="useBias">Whether overlap is pushed apart; the relax pass solves velocities only.</param>
    private void SolveContacts(float inverseH, float maxPush, bool useBias)
    {
        for (var c = 0; c < _count; c++)
        {
            ref var constraint = ref _constraints[c];
            ref var bodyA = ref _bodies[constraint.BodyA];
            ref var bodyB = ref _bodies[constraint.BodyB];
            var normal = constraint.Normal;
            var dp = bodyB.DP - bodyA.DP;

            for (var j = 0; j < constraint.PointCount; j++)
            {
                ref var cp = ref constraint.Points[j];
                var d = dp + bodyB.DQ.Rotate(cp.RB) - bodyA.DQ.Rotate(cp.RA);
                var separation = Vector2.Dot(d, normal) + cp.AdjustedSeparation;
                var bias = 0f;
                var massScale = 1f;
                var impulseScale = 0f;
                if (separation > 0)
                {
                    // Speculative: only stop the shapes from closing the remaining gap within this substep.
                    bias = separation * inverseH;
                }
                else if (useBias)
                {
                    bias = MathF.Max(constraint.Softness.BiasRate * separation, -maxPush);
                    massScale = constraint.Softness.MassScale;
                    impulseScale = constraint.Softness.ImpulseScale;
                }

                var vn = Vector2.Dot(RelativeVelocity(bodyA, bodyB, cp.RA, cp.RB), normal);
                var impulse = -cp.NormalMass * massScale * (vn + bias) - impulseScale * cp.NormalImpulse;
                var accumulated = MathF.Max(cp.NormalImpulse + impulse, 0);
                impulse = accumulated - cp.NormalImpulse;
                cp.NormalImpulse = accumulated;
                cp.MaxNormalImpulse = MathF.Max(cp.MaxNormalImpulse, impulse);
                Apply(ref bodyA, ref bodyB, constraint, cp.RA, cp.RB, impulse * normal);
            }

            var tangent = Vec.Cross(normal, 1f);
            for (var j = 0; j < constraint.PointCount; j++)
            {
                ref var cp = ref constraint.Points[j];
                var vt = Vector2.Dot(RelativeVelocity(bodyA, bodyB, cp.RA, cp.RB), tangent);
                var maxFriction = constraint.Friction * cp.NormalImpulse;
                var accumulated = Math.Clamp(cp.TangentImpulse - cp.TangentMass * vt, -maxFriction, maxFriction);
                var impulse = accumulated - cp.TangentImpulse;
                cp.TangentImpulse = accumulated;
                Apply(ref bodyA, ref bodyB, constraint, cp.RA, cp.RB, impulse * tangent);
            }
        }
    }

    private void IntegratePositions(float h, float maxTranslation)
    {
        for (var i = 0; i < _moverCount; i++)
        {
            ref var body = ref _bodies[_movers[i].Body];
            var translation = h * body.V;
            if (translation.LengthSquared() > maxTranslation * maxTranslation)
                body.V *= maxTranslation / translation.Length();
            var rotation = h * body.W;
            if (rotation * rotation > MaxRotationPerStep * MaxRotationPerStep)
                body.W *= MaxRotationPerStep / MathF.Abs(rotation);
            body.DP += h * body.V;
            if (body.W == 0)
                continue;
            var angle = h * body.W;
            body.DA += angle;
            var q = body.DQ;
            var c = q.Cos - angle * q.Sin;
            var s = q.Sin + angle * q.Cos;
            var inverseLength = 1 / MathF.Sqrt(c * c + s * s);
            body.DQ = new Rot(c * inverseLength, s * inverseLength);
        }
    }

    private void AccumulateImpulses()
    {
        for (var c = 0; c < _count; c++)
        {
            ref var constraint = ref _constraints[c];
            for (var j = 0; j < constraint.PointCount; j++)
                constraint.Points[j].TotalNormalImpulse += constraint.Points[j].NormalImpulse;
        }
    }

    /// <summary>Reverses the approach speed of points that collided fast enough, scaled by the contact's restitution.</summary>
    private void ApplyRestitution(float threshold)
    {
        for (var c = 0; c < _count; c++)
        {
            ref var constraint = ref _constraints[c];
            if (constraint.Restitution == 0)
                continue;
            ref var bodyA = ref _bodies[constraint.BodyA];
            ref var bodyB = ref _bodies[constraint.BodyB];
            var normal = constraint.Normal;
            for (var j = 0; j < constraint.PointCount; j++)
            {
                ref var cp = ref constraint.Points[j];
                if (cp.RelativeVelocity > -threshold || cp.MaxNormalImpulse == 0)
                    continue;
                var vn = Vector2.Dot(RelativeVelocity(bodyA, bodyB, cp.RA, cp.RB), normal);
                var impulse = -cp.NormalMass * (vn + constraint.Restitution * cp.RelativeVelocity);
                var accumulated = MathF.Max(cp.NormalImpulse + impulse, 0);
                impulse = accumulated - cp.NormalImpulse;
                cp.NormalImpulse = accumulated;
                cp.TotalNormalImpulse += impulse;
                Apply(ref bodyA, ref bodyB, constraint, cp.RA, cp.RB, impulse * normal);
            }
        }
    }

    private void StoreImpulses()
    {
        for (var c = 0; c < _count; c++)
        {
            ref var constraint = ref _constraints[c];
            ref var manifold = ref state.Contacts[constraint.Contact].Manifold;
            for (var j = 0; j < constraint.PointCount; j++)
            {
                ref var point = ref manifold.Points[j];
                ref var cp = ref constraint.Points[j];
                point.NormalImpulse = cp.NormalImpulse;
                point.TangentImpulse = cp.TangentImpulse;
                point.TotalNormalImpulse = cp.TotalNormalImpulse;
            }
        }
    }

    private void Finish()
    {
        var bodies = state.Bodies;
        for (var i = 0; i < _moverCount; i++)
        {
            ref var mover = ref _movers[i];
            ref var solverBody = ref _bodies[mover.Body];
            ref var body = ref bodies[mover.Body];
            body.Velocity = solverBody.V;
            body.AngularVelocity = solverBody.W;
            body.Center = body.Center0 + solverBody.DP;
            body.Angle = body.Angle0 + solverBody.DA;
            body.SyncOriginFromCenter();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 RelativeVelocity(in SolverBody bodyA, in SolverBody bodyB, Vector2 rA, Vector2 rB) =>
        bodyB.V + Vec.Cross(bodyB.W, rB) - bodyA.V - Vec.Cross(bodyA.W, rA);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Apply(ref SolverBody bodyA, ref SolverBody bodyB, in Constraint constraint, Vector2 rA, Vector2 rB, Vector2 p)
    {
        bodyA.V -= constraint.InvMassA * p;
        bodyA.W -= constraint.InvInertiaA * Vec.Cross(rA, p);
        bodyB.V += constraint.InvMassB * p;
        bodyB.W += constraint.InvInertiaB * Vec.Cross(rB, p);
    }

    /// <summary>The coefficients of a soft constraint that behaves like a damped spring of the given frequency.</summary>
    private readonly record struct Softness(float BiasRate, float MassScale, float ImpulseScale)
    {
        public static Softness Make(float hertz, float dampingRatio, float h)
        {
            if (hertz == 0)
                return new Softness(0, 1, 0);
            var omega = 2 * MathF.PI * hertz;
            var a1 = 2 * dampingRatio + h * omega;
            var a2 = h * omega * a1;
            var a3 = 1 / (1 + a2);
            return new Softness(omega / a1, a2 * a3, a3);
        }
    }

    /// <summary>The motion of a body during a step: velocity and the displacement and rotation since the step began.</summary>
    private struct SolverBody
    {
        public Vector2 V;
        public float W;
        public Vector2 DP;
        public float DA;
        public Rot DQ;
    }

    /// <summary>A body the substeps integrate, with its accelerations and damping precomputed.</summary>
    private struct Mover
    {
        public int Body;
        public bool Dynamic;
        public bool Rotates;
        public Vector2 Acceleration;
        public float AngularAcceleration;
        public float LinearDamping;
        public float AngularDamping;
    }

    private struct ConstraintPoint
    {
        public Vector2 RA;
        public Vector2 RB;
        public float AdjustedSeparation;
        public float NormalImpulse;
        public float TangentImpulse;
        public float NormalMass;
        public float TangentMass;

        /// <summary>The normal velocity before solving, which restitution reverses.</summary>
        public float RelativeVelocity;
        public float MaxNormalImpulse;
        public float TotalNormalImpulse;
    }

    [InlineArray(2)]
    private struct ConstraintPoints
    {
        private ConstraintPoint _element;
    }

    private struct Constraint
    {
        public int Contact;
        public int BodyA;
        public int BodyB;
        public Vector2 Normal;
        public float Friction;
        public float Restitution;
        public float InvMassA;
        public float InvInertiaA;
        public float InvMassB;
        public float InvInertiaB;
        public Softness Softness;
        public int PointCount;
        public ConstraintPoints Points;
    }
}
