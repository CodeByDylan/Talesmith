using System.Numerics;
using Microsoft.Extensions.Logging;
using Talesmith.Diagnostics;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Grids;
using Talesmith.Physics.Characters;
using Talesmith.Physics.Geometry;
using Talesmith.Physics.Simulation;
using Talesmith.Physics.Tiles;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;

namespace Talesmith.Physics;

/// <summary>The physics simulation of one scene's <see cref="World"/>; see <see cref="IPhysicsWorld"/>.</summary>
/// <remarks>
/// <see cref="Systems.PhysicsStepSystem"/> calls <see cref="Step"/> once per fixed update in play mode. A step mirrors the entities into
/// bodies, finds new pairs, updates contacts, solves velocities and positions, sweeps fast bodies, puts resting islands to sleep, writes
/// the results back to the entities and then delivers contact events. The same inputs always produce the same results.
/// </remarks>
public sealed class PhysicsWorld : IPhysicsWorld, IDisposable
{
    private readonly World _world;
    private readonly IEventBus _events;
    private readonly Profiler _profiler;
    private readonly PhysicsState _state;
    private readonly TileColliderCache _tiles;
    private readonly BodySynchronizer _bodies;
    private readonly ContactManager _contacts;
    private readonly ContactSolver _solver;
    private readonly ContinuousSolver _continuous;
    private readonly SleepSolver _sleep;
    private readonly QueryEngine _queries;
    private readonly CharacterMotor _motor;
    private readonly ParallelRunner _runner = new();
    private readonly Lock _listenerLock = new();
    private ICollisionListener[] _listeners = [];
    private Dictionary<Entity, ICollisionListener[]> _entityListeners = new();
    private bool _stepBegun;

    /// <param name="settings">Copied, so changing them later affects only worlds created afterwards.</param>
    public PhysicsWorld(World world, PhysicsSettings settings, IEventBus events, EngineProfilers profilers, ILogger<PhysicsWorld> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(profilers);
        _world = world;
        _events = events;
        _profiler = profilers.Game;
        Settings = settings.Clone();
        _state = new PhysicsState(Settings);
        _tiles = new TileColliderCache(_state, logger);
        _bodies = new BodySynchronizer(_state, logger);
        _contacts = new ContactManager(_state, _runner);
        _solver = new ContactSolver(_state);
        _continuous = new ContinuousSolver(_state);
        _sleep = new SleepSolver(_state);
        _queries = new QueryEngine(_state, _tiles);
        _motor = new CharacterMotor(_state, _tiles);
    }

    /// <summary>This world's settings; the broadphase and its margins are fixed when the world is created.</summary>
    public PhysicsSettings Settings { get; }

    public int BodyCount => _state.BodyCount;

    public int ContactCount => _state.ActiveContactCount;

    internal PhysicsState State => _state;

    /// <summary>Prepares a fixed step before game code runs in it: restores interpolated entities to their simulated pose and picks up
    /// colliders added or changed since the last step, so character moves and queries see them.</summary>
    /// <remarks><see cref="Step"/> calls it when <see cref="Systems.PhysicsPrepareSystem"/> did not.</remarks>
    public void BeginStep()
    {
        using (_profiler.Measure(PhysicsCounters.Sync))
        {
            _bodies.BeginStep(_world);
            _bodies.Sync(_world);
            _tiles.Sync(_world);
        }

        _stepBegun = true;
    }

    /// <summary>Advances the simulation by <paramref name="dt"/> seconds and delivers the step's contact events.</summary>
    public void Step(float dt)
    {
        if (dt <= 0)
            return;
        if (!_stepBegun)
            BeginStep();
        _stepBegun = false;
        _state.LastTimeStep = dt;

        using (_profiler.Measure(PhysicsCounters.Sync))
        {
            _bodies.Sync(_world);
            _tiles.Sync(_world);
            _tiles.Validate();
            EnsureTilesAroundMovedProxies();
        }

        using (_profiler.Measure(PhysicsCounters.Broadphase))
            _contacts.UpdatePairs();
        using (_profiler.Measure(PhysicsCounters.Narrowphase))
            _contacts.UpdateContacts(dt);
        using (_profiler.Measure(PhysicsCounters.Solve))
        {
            _solver.Solve(dt);
            _sleep.Update(dt);
        }

        using (_profiler.Measure(PhysicsCounters.Continuous))
            _continuous.Solve();
        int awake;
        using (_profiler.Measure(PhysicsCounters.Sync))
        {
            awake = SynchronizeMovedBodies();
            _bodies.WriteBack(_world);
        }

        _profiler.Set(PhysicsCounters.Bodies, _state.BodyCount);
        _profiler.Set(PhysicsCounters.AwakeBodies, awake);
        _profiler.Set(PhysicsCounters.Contacts, _state.ActiveContactCount);
        using (_profiler.Measure(PhysicsCounters.Events))
            DeliverEvents();
    }

    public void Dispose() => _runner.Dispose();

    /// <summary>Shows interpolated entities at <paramref name="alpha"/> between their last two fixed-step poses.</summary>
    public void Interpolate(float alpha) => _bodies.Interpolate(_world, Math.Clamp(alpha, 0, 1));

    public bool RayCast(Vector2 origin, Vector2 direction, float maxDistance, QueryFilter filter, out RaycastHit hit) =>
        _queries.RayCast(origin, direction, maxDistance, filter, out hit);

    public int RayCastAll(Vector2 origin, Vector2 direction, float maxDistance, QueryFilter filter, Span<RaycastHit> results) =>
        _queries.RayCastAll(origin, direction, maxDistance, filter, results);

    public bool ShapeCast(QueryShape shape, Vector2 position, float rotation, Vector2 direction, float maxDistance, QueryFilter filter, out RaycastHit hit) =>
        _queries.ShapeCast(shape, position, rotation, direction, maxDistance, filter, out hit);

    public int OverlapPoint(Vector2 point, QueryFilter filter, Span<Entity> results) => _queries.OverlapPoint(point, filter, results);

    public int OverlapShape(QueryShape shape, Vector2 position, float rotation, QueryFilter filter, Span<Entity> results) =>
        _queries.OverlapShape(shape, position, rotation, filter, results);

    public bool TryGetHitCell(in RaycastHit hit, out GridCoord cell) =>
        _tiles.TryGetCell(hit.Entity, hit.Point - hit.Normal * Settings.LinearSlop, out cell);

    public void AddForce(Entity entity, Vector2 force)
    {
        var id = DynamicBody(entity);
        if (id < 0)
            return;
        _state.Bodies[id].Force += force;
        _state.WakeBody(id);
    }

    public void AddForceAtPosition(Entity entity, Vector2 force, Vector2 point)
    {
        var id = DynamicBody(entity);
        if (id < 0)
            return;
        ref var body = ref _state.Bodies[id];
        body.Force += force;
        body.Torque += Vec.Cross(point - body.Center, force);
        _state.WakeBody(id);
    }

    public void AddTorque(Entity entity, float torque)
    {
        var id = DynamicBody(entity);
        if (id < 0)
            return;
        _state.Bodies[id].Torque += torque;
        _state.WakeBody(id);
    }

    public void AddImpulse(Entity entity, Vector2 impulse)
    {
        var id = DynamicBody(entity);
        if (id < 0)
            return;
        ref var body = ref _state.Bodies[id];
        ChangeVelocity(entity, id, body.Velocity + body.InvMass * impulse, body.AngularVelocity);
    }

    public void AddImpulseAtPosition(Entity entity, Vector2 impulse, Vector2 point)
    {
        var id = DynamicBody(entity);
        if (id < 0)
            return;
        ref var body = ref _state.Bodies[id];
        ChangeVelocity(entity, id, body.Velocity + body.InvMass * impulse, body.AngularVelocity + body.InvInertia * Vec.Cross(point - body.Center, impulse));
    }

    public void AddAngularImpulse(Entity entity, float impulse)
    {
        var id = DynamicBody(entity);
        if (id < 0)
            return;
        ref var body = ref _state.Bodies[id];
        ChangeVelocity(entity, id, body.Velocity, body.AngularVelocity + body.InvInertia * impulse);
    }

    public void MovePosition(Entity entity, Vector2 position)
    {
        var id = _bodies.Ensure(_world, entity);
        if (id < 0)
            return;
        ref var body = ref _state.Bodies[id];
        if (body.Kind != BodyKind.Kinematic || body.Has(BodyFlags.Character))
        {
            _world.Get<Transform>(entity).Position = position;
            return;
        }

        if (!body.Has(BodyFlags.HasMoveTarget))
            body.MoveTargetAngle = body.Angle;
        body.MoveTarget = position;
        body.Flags |= BodyFlags.HasMoveTarget;
    }

    public void MoveRotation(Entity entity, float rotation)
    {
        var id = _bodies.Ensure(_world, entity);
        if (id < 0)
            return;
        ref var body = ref _state.Bodies[id];
        if (body.Kind != BodyKind.Kinematic || body.Has(BodyFlags.Character))
        {
            _world.Get<Transform>(entity).Rotation = rotation;
            return;
        }

        if (!body.Has(BodyFlags.HasMoveTarget))
            body.MoveTarget = body.Origin;
        body.MoveTargetAngle = rotation;
        body.Flags |= BodyFlags.HasMoveTarget;
    }

    public void WakeUp(Entity entity)
    {
        var id = _bodies.Ensure(_world, entity);
        if (id >= 0)
            _state.WakeBody(id);
    }

    public bool IsAwake(Entity entity)
    {
        var id = _state.BodyOf(entity);
        return id >= 0 && _state.Bodies[id].IsAwake;
    }

    /// <exception cref="InvalidOperationException">The entity has no <see cref="CharacterController2D"/> or <see cref="Transform"/>.</exception>
    public CharacterCollisions MoveCharacter(Entity entity, Vector2 motion)
    {
        if (!_world.IsAlive(entity) || !_world.Has<CharacterController2D>(entity) || !_world.Has<Transform>(entity))
            throw new InvalidOperationException($"{entity} needs a {nameof(CharacterController2D)} and a {nameof(Transform)} to be moved as a character.");
        var id = _bodies.Ensure(_world, entity);
        return _motor.Move(id, ref _world.Get<CharacterController2D>(entity), ref _world.Get<Transform>(entity), motion);
    }

    public int GetContacts(Entity entity, Span<ContactInfo> results)
    {
        var id = _state.BodyOf(entity);
        if (id < 0)
            return 0;
        var count = 0;
        for (var c = _state.Bodies[id].FirstContactEdge; c != PhysicsState.Null && count < results.Length; c = _state.Contacts[c].NextOf(id))
        {
            if (_state.Contacts[c].IsReported)
                results[count++] = _state.Describe(c).For(entity);
        }

        return count;
    }

    public IDisposable AddCollisionListener(ICollisionListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_listenerLock)
            _listeners = [.. _listeners, listener];
        return new Subscription(() =>
        {
            lock (_listenerLock)
                _listeners = Array.FindAll(_listeners, l => !ReferenceEquals(l, listener));
        });
    }

    public IDisposable AddCollisionListener(Entity entity, ICollisionListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        lock (_listenerLock)
        {
            var copy = new Dictionary<Entity, ICollisionListener[]>(_entityListeners);
            copy[entity] = copy.TryGetValue(entity, out var existing) ? [.. existing, listener] : [listener];
            _entityListeners = copy;
        }

        return new Subscription(() =>
        {
            lock (_listenerLock)
            {
                if (!_entityListeners.TryGetValue(entity, out var existing))
                    return;
                var copy = new Dictionary<Entity, ICollisionListener[]>(_entityListeners);
                var remaining = Array.FindAll(existing, l => !ReferenceEquals(l, listener));
                if (remaining.Length == 0)
                    copy.Remove(entity);
                else
                    copy[entity] = remaining;
                _entityListeners = copy;
            }
        });
    }

    private int DynamicBody(Entity entity)
    {
        var id = _bodies.Ensure(_world, entity);
        return id >= 0 && _state.Bodies[id].Kind == BodyKind.Dynamic ? id : PhysicsState.Null;
    }

    private void ChangeVelocity(Entity entity, int id, Vector2 velocity, float angularVelocity)
    {
        ref var body = ref _state.Bodies[id];
        body.Velocity = velocity;
        body.AngularVelocity = angularVelocity;
        _state.WakeBody(id);
        ref var rigidbody = ref _world.TryGetRef<Rigidbody2D>(entity, out var exists);
        if (!exists)
            return;
        rigidbody.Velocity = velocity;
        rigidbody.AngularVelocity = angularVelocity;
        body.WrittenVelocity = velocity;
        body.WrittenAngularVelocity = angularVelocity;
    }

    private void EnsureTilesAroundMovedProxies()
    {
        if (_tiles.MapCount == 0)
            return;
        var broadphase = _state.Broadphase;
        var count = _state.MoveCount;
        for (var i = 0; i < count; i++)
        {
            var proxy = _state.MoveBuffer[i];
            if (!_state.IsBufferedMove(proxy))
                continue;
            var body = _state.Fixtures[broadphase.GetUserData(proxy)].Body;
            if (_state.Bodies[body].Kind != BodyKind.Static)
                _tiles.EnsureRegion(broadphase.GetFatAabb(proxy));
        }
    }

    /// <returns>The number of awake bodies.</returns>
    private int SynchronizeMovedBodies()
    {
        var awake = 0;
        for (var i = 0; i < _state.BodyHighWater; i++)
        {
            ref var body = ref _state.Bodies[i];
            if (!body.IsAlive || body.Kind == BodyKind.Static)
            {
                body.Flags &= ~BodyFlags.Moved;
                continue;
            }

            if (body.IsAwake)
                awake++;
            if (body.Center != body.Center0 || body.Angle != body.Angle0)
                _state.SynchronizeFixtures(i, body.Center - body.Center0);
            if (body.Has(BodyFlags.Character))
            {
                body.Velocity = Vector2.Zero;
                body.AngularVelocity = 0;
            }

            body.Force = Vector2.Zero;
            body.Torque = 0;
            body.Flags &= ~(BodyFlags.Moved | BodyFlags.HasMoveTarget);
        }

        return awake;
    }

    private void DeliverEvents()
    {
        var count = _state.EventCount;
        if (count == 0)
            return;
        var collisionEntered = _events.HasSubscribers<CollisionEntered>();
        var collisionStayed = _events.HasSubscribers<CollisionStayed>();
        var collisionExited = _events.HasSubscribers<CollisionExited>();
        var triggerEntered = _events.HasSubscribers<PhysicsTriggerEntered>();
        var triggerStayed = _events.HasSubscribers<PhysicsTriggerStayed>();
        var triggerExited = _events.HasSubscribers<PhysicsTriggerExited>();
        var listeners = _listeners;
        var entityListeners = _entityListeners;
        var notify = listeners.Length > 0 || entityListeners.Count > 0;
        for (var i = 0; i < count; i++)
        {
            var e = _state.Events[i];
            var published = e.Kind switch
            {
                ContactEventKind.CollisionEntered => collisionEntered,
                ContactEventKind.CollisionStayed => collisionStayed,
                ContactEventKind.CollisionExited => collisionExited,
                ContactEventKind.TriggerEntered => triggerEntered,
                ContactEventKind.TriggerStayed => triggerStayed,
                _ => triggerExited
            };
            if (!published && !notify)
                continue;
            if (e.Contact != PhysicsState.Null)
                e.Data = _state.Describe(e.Contact);
            switch (e.Kind)
            {
                case ContactEventKind.CollisionEntered when collisionEntered:
                    _events.Publish(new CollisionEntered(e.Data));
                    break;
                case ContactEventKind.CollisionStayed when collisionStayed:
                    _events.Publish(new CollisionStayed(e.Data));
                    break;
                case ContactEventKind.CollisionExited when collisionExited:
                    _events.Publish(new CollisionExited(e.Data));
                    break;
                case ContactEventKind.TriggerEntered when triggerEntered:
                    _events.Publish(new PhysicsTriggerEntered(e.Data));
                    break;
                case ContactEventKind.TriggerStayed when triggerStayed:
                    _events.Publish(new PhysicsTriggerStayed(e.Data));
                    break;
                case ContactEventKind.TriggerExited when triggerExited:
                    _events.Publish(new PhysicsTriggerExited(e.Data));
                    break;
            }

            if (!notify)
                continue;
            Notify(e.Kind, e.Data.For(e.Data.A), listeners, entityListeners);
            Notify(e.Kind, e.Data.For(e.Data.B), listeners, entityListeners);
        }

        _state.EventCount = 0;
    }

    private static void Notify(ContactEventKind kind, in ContactInfo contact, ICollisionListener[] listeners, Dictionary<Entity, ICollisionListener[]> entityListeners)
    {
        foreach (var listener in listeners)
            Notify(kind, contact, listener);
        if (entityListeners.Count > 0 && entityListeners.TryGetValue(contact.Self, out var own))
        {
            foreach (var listener in own)
                Notify(kind, contact, listener);
        }
    }

    private static void Notify(ContactEventKind kind, in ContactInfo contact, ICollisionListener listener)
    {
        switch (kind)
        {
            case ContactEventKind.CollisionEntered:
                listener.OnCollisionEntered(contact);
                break;
            case ContactEventKind.CollisionStayed:
                listener.OnCollisionStayed(contact);
                break;
            case ContactEventKind.CollisionExited:
                listener.OnCollisionExited(contact);
                break;
            case ContactEventKind.TriggerEntered:
                listener.OnTriggerEntered(contact);
                break;
            case ContactEventKind.TriggerStayed:
                listener.OnTriggerStayed(contact);
                break;
            case ContactEventKind.TriggerExited:
                listener.OnTriggerExited(contact);
                break;
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
