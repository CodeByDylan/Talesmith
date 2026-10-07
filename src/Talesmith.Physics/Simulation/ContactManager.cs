using System.Numerics;
using Talesmith.Physics.Broadphase;
using Talesmith.Physics.Collision;

namespace Talesmith.Physics.Simulation;

/// <summary>Creates contacts for new overlapping pairs, updates their manifolds and records begin, stay and end events.</summary>
/// <remarks>Manifolds are computed in parallel, each from its own two shapes only, and applied in contact order, so results stay deterministic.</remarks>
internal sealed class ContactManager
{
    private readonly PhysicsState _state;
    private readonly ParallelRunner _runner;
    private readonly CollideJob _collideJob;
    private ulong[] _candidates = new ulong[256];
    private int _candidateCount;
    private int[] _updates = new int[256];
    private Manifold[] _manifolds = new Manifold[256];
    private int _updateCount;

    public ContactManager(PhysicsState state, ParallelRunner runner)
    {
        _state = state;
        _runner = runner;
        _collideJob = new CollideJob(this);
    }

    /// <summary>Finds pairs involving proxies that moved since the last call and creates their contacts, in a deterministic order.</summary>
    public void UpdatePairs()
    {
        var broadphase = _state.Broadphase;
        _candidateCount = 0;
        for (var i = 0; i < _state.MoveCount; i++)
        {
            var proxy = _state.MoveBuffer[i];
            if (!_state.IsBufferedMove(proxy))
                continue;
            var query = new PairQuery(this, proxy, broadphase.GetUserData(proxy));
            broadphase.Query(broadphase.GetFatAabb(proxy), ref query);
        }

        _state.ClearMoveBuffer();
        if (_candidateCount == 0)
            return;

        Array.Sort(_candidates, 0, _candidateCount);
        var previous = ulong.MaxValue;
        for (var i = 0; i < _candidateCount; i++)
        {
            var key = _candidates[i];
            if (key == previous)
                continue;
            previous = key;
            var fixtureA = (int)(key >> 32);
            var fixtureB = (int)(uint)key;
            if (_state.Pairs.ContainsKey(key) || !_state.ShouldCollide(fixtureA, fixtureB))
                continue;
            _state.CreateContact(fixtureA, fixtureB);
        }
    }

    /// <summary>Recomputes the manifolds of contacts with an awake or moved body and records contact events.</summary>
    public void UpdateContacts(float dt)
    {
        var broadphase = _state.Broadphase;
        var settings = _state.Settings;
        var slop = settings.LinearSlop;
        _updateCount = 0;
        for (var i = 0; i < _state.ActiveContactCount;)
        {
            var id = _state.ActiveContacts[i];
            ref var contact = ref _state.Contacts[id];
            var activeA = (_state.Bodies[contact.BodyA].Flags & (BodyFlags.Awake | BodyFlags.Moved)) != 0;
            var activeB = (_state.Bodies[contact.BodyB].Flags & (BodyFlags.Awake | BodyFlags.Moved)) != 0;
            if (!activeA && !activeB)
            {
                i++;
                continue;
            }

            if (!broadphase.GetFatAabb(_state.Fixtures[contact.FixtureA].Proxy).Overlaps(broadphase.GetFatAabb(_state.Fixtures[contact.FixtureB].Proxy)))
            {
                _state.DestroyContact(id, wake: false);
                continue;
            }

            if (_updateCount == _updates.Length)
            {
                Array.Resize(ref _updates, _updates.Length * 2);
                Array.Resize(ref _manifolds, _updates.Length);
            }

            _updates[_updateCount++] = id;
            i++;
        }

        _collideJob.Speculative = settings.SpeculativeDistance;
        _collideJob.Slop = slop;
        _runner.For(_updateCount, _collideJob);

        for (var u = 0; u < _updateCount; u++)
            Apply(_updates[u], ref _manifolds[u], dt, slop, settings.WarmStarting);
    }

    /// <summary>Stores a new manifold in a contact and records the events and wake-ups it causes.</summary>
    private void Apply(int id, ref Manifold manifold, float dt, float slop, bool warmStarting)
    {
        ref var contact = ref _state.Contacts[id];
        ref var fixtureA = ref _state.Fixtures[contact.FixtureA];
        ref var fixtureB = ref _state.Fixtures[contact.FixtureB];
        ref var bodyA = ref _state.Bodies[contact.BodyA];
        ref var bodyB = ref _state.Bodies[contact.BodyB];
        var wasTouching = contact.IsTouching;
        var sensor = contact.IsSensor;
        var touching = sensor ? manifold.PointCount > 0 && manifold.MinSeparation < 0 : manifold.PointCount > 0;
        if (!sensor && warmStarting)
            MatchImpulses(ref manifold, contact.Manifold);
        contact.Manifold = manifold;
        contact.Flags = touching ? contact.Flags | ContactFlags.Touching : contact.Flags & ~ContactFlags.Touching;

        if (!sensor && (fixtureA.OneWay || fixtureB.OneWay))
            UpdateOneWay(ref contact, wasTouching, dt, slop);

        // Shapes closer than twice the slop count as touching, which includes characters resting at their skin width.
        var report = touching && !contact.IsDisabled && (sensor || manifold.MinSeparation < 2 * slop);
        if (report != contact.IsReported)
        {
            if (report)
            {
                contact.Flags |= ContactFlags.Reported;
                _state.AddEvent(sensor ? ContactEventKind.TriggerEntered : ContactEventKind.CollisionEntered, id, default);
            }
            else
            {
                _state.AddEvent(sensor ? ContactEventKind.TriggerExited : ContactEventKind.CollisionExited, PhysicsState.Null, _state.Describe(id));
                contact.Flags &= ~ContactFlags.Reported;
            }
        }
        else if (report)
        {
            _state.AddEvent(sensor ? ContactEventKind.TriggerStayed : ContactEventKind.CollisionStayed, id, default);
        }

        if (contact.IsSolid)
        {
            if (bodyA.Kind == BodyKind.Dynamic && !bodyA.IsAwake && IsPushing(bodyB))
                _state.WakeBody(contact.BodyA);
            else if (bodyB.Kind == BodyKind.Dynamic && !bodyB.IsAwake && IsPushing(bodyA))
                _state.WakeBody(contact.BodyB);
        }
    }

    private static bool IsPushing(in Body body) =>
        body.Has(BodyFlags.Moved) || (body.IsAwake && (body.Kind == BodyKind.Dynamic || body.Velocity != Vector2.Zero || body.AngularVelocity != 0));

    private static void MatchImpulses(ref Manifold manifold, in Manifold old)
    {
        for (var i = 0; i < manifold.PointCount; i++)
        {
            ref var point = ref manifold.Points[i];
            for (var j = 0; j < old.PointCount; j++)
            {
                if (old.Points[j].Id != point.Id)
                    continue;
                point.NormalImpulse = old.Points[j].NormalImpulse;
                point.TangentImpulse = old.Points[j].TangentImpulse;
                point.Persisted = true;
                break;
            }
        }
    }

    /// <summary>Lets shapes pass through one-way fixtures unless they arrive from the fixture's up side.</summary>
    /// <remarks>A contact that starts from the wrong side stays disabled until the shapes separate, so bodies jump through cleanly.</remarks>
    private void UpdateOneWay(ref Contact contact, bool wasTouching, float dt, float slop)
    {
        ref var manifold = ref contact.Manifold;
        if (contact.IsDisabled)
        {
            if (manifold.PointCount == 0 || manifold.MinSeparation >= 0)
                contact.Flags &= ~ContactFlags.Disabled;
            else
                return;
        }

        if (manifold.PointCount == 0 || wasTouching)
            return;

        var platformIsA = _state.Fixtures[contact.FixtureA].OneWay;
        ref var platformFixture = ref _state.Fixtures[platformIsA ? contact.FixtureA : contact.FixtureB];
        ref var platform = ref _state.Bodies[platformIsA ? contact.BodyA : contact.BodyB];
        ref var other = ref _state.Bodies[platformIsA ? contact.BodyB : contact.BodyA];
        var up = platform.Q.Rotate(platformFixture.LocalUp);
        var normal = platformIsA ? manifold.Normal : -manifold.Normal;
        var approach = MathF.Max(0, -Vector2.Dot(other.Velocity - platform.Velocity, up)) * dt;
        var fromAbove = Vector2.Dot(normal, up) > 0.5f && -manifold.MinSeparation <= 3 * slop + approach;
        if (!fromAbove)
            contact.Flags |= ContactFlags.Disabled;
    }

    private void AddCandidate(int fixtureA, int fixtureB)
    {
        if (_candidateCount == _candidates.Length)
            Array.Resize(ref _candidates, _candidates.Length * 2);
        _candidates[_candidateCount++] = PhysicsState.PairKey(fixtureA, fixtureB);
    }

    private readonly struct PairQuery(ContactManager manager, int proxy, int fixture) : IProxyQuery
    {
        public bool Report(int other, int userData)
        {
            if (other == proxy)
                return true;
            if (other > proxy && manager.IsMoved(other))
                return true;
            manager.AddCandidate(fixture, userData);
            return true;
        }
    }

    private bool IsMoved(int proxy) => _state.IsBufferedMove(proxy);

    private sealed class CollideJob(ContactManager manager) : IRangeJob
    {
        public float Speculative;
        public float Slop;

        public void Execute(int start, int end)
        {
            var physics = manager._state;
            var updates = manager._updates;
            var manifolds = manager._manifolds;
            for (var u = start; u < end; u++)
            {
                ref var contact = ref physics.Contacts[updates[u]];
                ref var fixtureA = ref physics.Fixtures[contact.FixtureA];
                ref var fixtureB = ref physics.Fixtures[contact.FixtureB];
                if (fixtureA.Aabb.Inflate(Speculative).Overlaps(fixtureB.Aabb))
                    Collide.Shapes(fixtureA.World, fixtureB.World, Speculative, Slop, out manifolds[u]);
                else
                    manifolds[u] = default;
            }
        }
    }
}
