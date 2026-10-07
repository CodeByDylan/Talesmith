using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Physics.Broadphase;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Simulation;

/// <summary>The bodies, fixtures and contacts of a physics world and the operations that keep them consistent.</summary>
/// <remarks>
/// Everything lives in pooled arrays addressed by index, so stepping does not allocate. Iteration orders depend only on the order of
/// creation, which keeps the simulation deterministic for the same inputs.
/// </remarks>
internal sealed class PhysicsState
{
    public const int Null = -1;

    private int _freeBody = Null;
    private int _freeFixture = Null;
    private int _freeContact = Null;
    private int[] _proxyMoveStamp = new int[64];
    private int _moveStamp = 1;

    public PhysicsState(PhysicsSettings settings)
    {
        Settings = settings;
        Broadphase = settings.Broadphase == BroadphaseKind.SpatialHash
            ? new SpatialHash(settings.AabbMargin, settings.SpatialHashCellSize)
            : new DynamicTree(settings.AabbMargin);
    }

    public PhysicsSettings Settings { get; }

    public IBroadphase Broadphase { get; }

    public Body[] Bodies = new Body[64];
    public int BodyHighWater;
    public int BodyCount;

    public Fixture[] Fixtures = new Fixture[64];
    public int FixtureHighWater;

    public Contact[] Contacts = new Contact[64];
    public int ContactHighWater;
    public int[] ActiveContacts = new int[64];
    public int ActiveContactCount;
    public readonly Dictionary<ulong, int> Pairs = new();

    /// <summary>The body of each entity, indexed by entity id.</summary>
    public int[] BodyOfEntity = CreateFilled(256);

    public ContactEvent[] Events = new ContactEvent[64];
    public int EventCount;

    public int[] MoveBuffer = new int[64];
    public int MoveCount;

    public float LastTimeStep = 1f / 60;

    /// <summary>Finds the body of an entity, or <see cref="Null"/>.</summary>
    public int BodyOf(Entity entity)
    {
        if ((uint)entity.Id >= (uint)BodyOfEntity.Length)
            return Null;
        var body = BodyOfEntity[entity.Id];
        return body != Null && Bodies[body].Entity == entity && !Bodies[body].Has(BodyFlags.TileChunk) ? body : Null;
    }

    public int CreateBody(Entity entity, BodyKind kind, BodyFlags flags, Vector2 origin, float angle)
    {
        int id;
        if (_freeBody != Null)
        {
            id = _freeBody;
            _freeBody = Bodies[id].NextFree;
        }
        else
        {
            if (BodyHighWater == Bodies.Length)
                Array.Resize(ref Bodies, Bodies.Length * 2);
            id = BodyHighWater++;
        }

        ref var body = ref Bodies[id];
        body = new Body
        {
            Entity = entity,
            Kind = kind,
            Flags = flags | BodyFlags.Alive | (kind == BodyKind.Static ? BodyFlags.None : BodyFlags.Awake),
            FirstFixture = Null,
            FirstContactEdge = Null,
            NextFree = Null,
            GravityScale = 1
        };
        body.SetPose(origin, angle);
        body.PreviousOrigin = origin;
        body.PreviousAngle = angle;
        body.Center0 = body.Center;
        body.Angle0 = angle;
        BodyCount++;

        if ((flags & BodyFlags.TileChunk) == 0)
        {
            if (entity.Id >= BodyOfEntity.Length)
            {
                var grown = CreateFilled(Math.Max(BodyOfEntity.Length * 2, entity.Id + 1));
                BodyOfEntity.CopyTo(grown, 0);
                BodyOfEntity = grown;
            }

            BodyOfEntity[entity.Id] = id;
        }

        return id;
    }

    public void DestroyBody(int id)
    {
        ref var body = ref Bodies[id];
        while (body.FirstFixture != Null)
            DestroyFixture(body.FirstFixture);
        if (!body.Has(BodyFlags.TileChunk) && (uint)body.Entity.Id < (uint)BodyOfEntity.Length && BodyOfEntity[body.Entity.Id] == id)
            BodyOfEntity[body.Entity.Id] = Null;
        body.Flags = BodyFlags.None;
        body.Entity = Entity.Null;
        body.NextFree = _freeBody;
        _freeBody = id;
        BodyCount--;
    }

    public int CreateFixture(int bodyId, in Shape local, in FixtureOptions options)
    {
        int id;
        if (_freeFixture != Null)
        {
            id = _freeFixture;
            _freeFixture = Fixtures[id].Next;
        }
        else
        {
            if (FixtureHighWater == Fixtures.Length)
                Array.Resize(ref Fixtures, Fixtures.Length * 2);
            id = FixtureHighWater++;
        }

        ref var body = ref Bodies[bodyId];
        ref var fixture = ref Fixtures[id];
        fixture = new Fixture
        {
            Body = bodyId,
            Next = body.FirstFixture,
            Local = local,
            World = local.Transform(body.Transform),
            Friction = options.Friction,
            Restitution = options.Restitution,
            Density = options.Density,
            Layer = options.Layer,
            Mask = options.Mask,
            IsTrigger = options.IsTrigger,
            OneWay = options.OneWay && !options.IsTrigger,
            LocalUp = options.LocalUp,
            Alive = true
        };
        fixture.Aabb = fixture.World.ComputeAabb();
        fixture.Proxy = Broadphase.CreateProxy(fixture.Aabb, id);
        body.FirstFixture = id;
        BufferMove(fixture.Proxy);
        return id;
    }

    public void DestroyFixture(int id)
    {
        ref var fixture = ref Fixtures[id];
        ref var body = ref Bodies[fixture.Body];
        var edge = body.FirstContactEdge;
        while (edge != Null)
        {
            var next = Contacts[edge].NextOf(fixture.Body);
            if (Contacts[edge].FixtureA == id || Contacts[edge].FixtureB == id)
                DestroyContact(edge, wake: true);
            edge = next;
        }

        if (body.FirstFixture == id)
        {
            body.FirstFixture = fixture.Next;
        }
        else
        {
            var previous = body.FirstFixture;
            while (Fixtures[previous].Next != id)
                previous = Fixtures[previous].Next;
            Fixtures[previous].Next = fixture.Next;
        }

        if ((uint)fixture.Proxy < (uint)_proxyMoveStamp.Length)
            _proxyMoveStamp[fixture.Proxy] = 0;
        Broadphase.DestroyProxy(fixture.Proxy);
        fixture.Alive = false;
        fixture.Proxy = Null;
        fixture.Next = _freeFixture;
        _freeFixture = id;
    }

    /// <summary>Recomputes the world shapes and bounds of a body's fixtures after it moved by <paramref name="displacement"/>.</summary>
    public void SynchronizeFixtures(int bodyId, Vector2 displacement)
    {
        ref var body = ref Bodies[bodyId];
        var xf = body.Transform;
        for (var f = body.FirstFixture; f != Null; f = Fixtures[f].Next)
        {
            ref var fixture = ref Fixtures[f];
            fixture.World = fixture.Local.Transform(xf);
            fixture.Aabb = fixture.World.ComputeAabb();
            if (Broadphase.MoveProxy(fixture.Proxy, fixture.Aabb, displacement))
                BufferMove(fixture.Proxy);
        }
    }

    public void BufferMove(int proxy)
    {
        if (proxy >= _proxyMoveStamp.Length)
            Array.Resize(ref _proxyMoveStamp, Math.Max(_proxyMoveStamp.Length * 2, proxy + 1));
        if (_proxyMoveStamp[proxy] == _moveStamp)
            return;
        _proxyMoveStamp[proxy] = _moveStamp;
        if (MoveCount == MoveBuffer.Length)
            Array.Resize(ref MoveBuffer, MoveBuffer.Length * 2);
        MoveBuffer[MoveCount++] = proxy;
    }

    /// <summary>Whether a buffered proxy still exists and moved since the buffer was last cleared.</summary>
    public bool IsBufferedMove(int proxy) => (uint)proxy < (uint)_proxyMoveStamp.Length && _proxyMoveStamp[proxy] == _moveStamp;

    public void ClearMoveBuffer()
    {
        MoveCount = 0;
        _moveStamp++;
    }

    /// <summary>Recomputes mass, center of mass, inertia and extents from the fixtures.</summary>
    public void UpdateMass(int bodyId, float rigidbodyMass, bool autoMass)
    {
        ref var body = ref Bodies[bodyId];
        body.RigidbodyMass = rigidbodyMass;
        body.RigidbodyAutoMass = autoMass;
        var mass = 0f;
        var center = Vector2.Zero;
        var inertia = 0f;
        for (var f = body.FirstFixture; f != Null; f = Fixtures[f].Next)
        {
            ref var fixture = ref Fixtures[f];
            if (fixture.IsTrigger)
                continue;
            var data = fixture.Local.ComputeMass(MathF.Max(fixture.Density, 0));
            mass += data.Mass;
            center += data.Mass * data.Center;
            inertia += data.Inertia;
        }

        if (body.Kind != BodyKind.Dynamic)
        {
            body.Mass = body.InvMass = body.Inertia = body.InvInertia = 0;
            body.LocalCenter = Vector2.Zero;
            body.Center = body.Origin;
            ComputeExtents(ref body);
            return;
        }

        if (mass > 0)
            center /= mass;
        if (!autoMass || mass <= 0)
        {
            var target = MathF.Max(rigidbodyMass, 1e-4f);
            inertia = mass > 0 ? inertia * (target / mass) : 0;
            mass = target;
        }

        body.Mass = mass;
        body.InvMass = 1 / mass;
        body.LocalCenter = center;
        body.Center = body.Origin + body.Q.Rotate(center);
        ComputeExtents(ref body);
        var centerInertia = inertia - mass * Vector2.Dot(center, center);
        if (body.Has(BodyFlags.FixedRotation) || centerInertia <= 0)
        {
            body.Inertia = 0;
            body.InvInertia = 0;
        }
        else
        {
            body.Inertia = centerInertia;
            body.InvInertia = 1 / centerInertia;
        }
    }

    public void WakeBody(int bodyId)
    {
        ref var body = ref Bodies[bodyId];
        if (body.Kind == BodyKind.Static || body.IsAwake)
            return;
        body.Flags |= BodyFlags.Awake;
        body.SleepTime = 0;
    }

    public void SleepBody(int bodyId)
    {
        ref var body = ref Bodies[bodyId];
        body.Flags &= ~BodyFlags.Awake;
        body.SleepTime = 0;
        body.Velocity = Vector2.Zero;
        body.AngularVelocity = 0;
        body.Force = Vector2.Zero;
        body.Torque = 0;
    }

    public bool ShouldCollide(int fixtureA, int fixtureB)
    {
        ref var a = ref Fixtures[fixtureA];
        ref var b = ref Fixtures[fixtureB];
        if (a.Body == b.Body)
            return false;
        var kindA = Bodies[a.Body].Kind;
        var kindB = Bodies[b.Body].Kind;
        if (kindA != BodyKind.Dynamic && kindB != BodyKind.Dynamic)
        {
            if (!a.IsTrigger && !b.IsTrigger)
                return false;
            if (kindA == BodyKind.Static && kindB == BodyKind.Static)
                return false;
        }

        return PhysicsLayers.Contains(a.Mask, b.Layer) && PhysicsLayers.Contains(b.Mask, a.Layer) && Settings.ShouldLayersCollide(a.Layer, b.Layer);
    }

    public int CreateContact(int fixtureA, int fixtureB)
    {
        if (fixtureA > fixtureB)
            (fixtureA, fixtureB) = (fixtureB, fixtureA);
        int id;
        if (_freeContact != Null)
        {
            id = _freeContact;
            _freeContact = Contacts[id].NextFree;
        }
        else
        {
            if (ContactHighWater == Contacts.Length)
                Array.Resize(ref Contacts, Contacts.Length * 2);
            id = ContactHighWater++;
        }

        ref var a = ref Fixtures[fixtureA];
        ref var b = ref Fixtures[fixtureB];
        ref var contact = ref Contacts[id];
        contact = new Contact
        {
            FixtureA = fixtureA,
            FixtureB = fixtureB,
            BodyA = a.Body,
            BodyB = b.Body,
            Flags = a.IsTrigger || b.IsTrigger ? ContactFlags.Sensor : ContactFlags.None,
            Friction = MathF.Sqrt(MathF.Max(0, a.Friction * b.Friction)),
            Restitution = MathF.Max(a.Restitution, b.Restitution),
            PrevA = Null,
            PrevB = Null,
            NextFree = Null,
            Alive = true
        };

        LinkEdge(id, a.Body, isA: true);
        LinkEdge(id, b.Body, isA: false);
        if (ActiveContactCount == ActiveContacts.Length)
            Array.Resize(ref ActiveContacts, ActiveContacts.Length * 2);
        contact.ActiveIndex = ActiveContactCount;
        ActiveContacts[ActiveContactCount++] = id;
        Pairs[PairKey(fixtureA, fixtureB)] = id;
        return id;
    }

    public void DestroyContact(int id, bool wake)
    {
        ref var contact = ref Contacts[id];
        if (contact.IsReported)
        {
            AddEvent(contact.IsSensor ? ContactEventKind.TriggerExited : ContactEventKind.CollisionExited, Null, Describe(id));
            if (wake && !contact.IsSensor)
            {
                WakeBody(contact.BodyA);
                WakeBody(contact.BodyB);
            }
        }

        UnlinkEdge(id, contact.BodyA, contact.PrevA, contact.NextA);
        UnlinkEdge(id, contact.BodyB, contact.PrevB, contact.NextB);
        Pairs.Remove(PairKey(contact.FixtureA, contact.FixtureB));

        var last = ActiveContacts[--ActiveContactCount];
        ActiveContacts[contact.ActiveIndex] = last;
        Contacts[last].ActiveIndex = contact.ActiveIndex;

        contact.Alive = false;
        contact.Flags = ContactFlags.None;
        contact.NextFree = _freeContact;
        _freeContact = id;
    }

    public static ulong PairKey(int fixtureA, int fixtureB) =>
        fixtureA < fixtureB ? (ulong)(uint)fixtureA << 32 | (uint)fixtureB : (ulong)(uint)fixtureB << 32 | (uint)fixtureA;

    public void AddEvent(ContactEventKind kind, int contact, in PhysicsContact data)
    {
        if (EventCount == Events.Length)
            Array.Resize(ref Events, Events.Length * 2);
        Events[EventCount++] = new ContactEvent(kind, contact, data);
    }

    /// <summary>The public description of a contact: entities, average point, normal, total impulse and relative velocity.</summary>
    public PhysicsContact Describe(int id)
    {
        ref var contact = ref Contacts[id];
        ref var bodyA = ref Bodies[contact.BodyA];
        ref var bodyB = ref Bodies[contact.BodyB];
        ref var manifold = ref contact.Manifold;
        Vector2 point;
        var impulse = 0f;
        if (manifold.PointCount > 0)
        {
            point = Vector2.Zero;
            for (var i = 0; i < manifold.PointCount; i++)
            {
                point += manifold.Points[i].Point;
                impulse += manifold.Points[i].TotalNormalImpulse;
            }

            point /= manifold.PointCount;
        }
        else
        {
            point = (Fixtures[contact.FixtureA].Aabb.Center + Fixtures[contact.FixtureB].Aabb.Center) * 0.5f;
        }

        var velocityA = bodyA.Velocity + Vec.Cross(bodyA.AngularVelocity, point - bodyA.Center);
        var velocityB = bodyB.Velocity + Vec.Cross(bodyB.AngularVelocity, point - bodyB.Center);
        return new PhysicsContact(bodyA.Entity, bodyB.Entity, point, manifold.Normal, contact.IsSensor ? 0 : impulse, velocityB - velocityA, contact.IsSensor);
    }

    private void LinkEdge(int id, int bodyId, bool isA)
    {
        ref var body = ref Bodies[bodyId];
        var head = body.FirstContactEdge;
        if (isA)
            Contacts[id].NextA = head;
        else
            Contacts[id].NextB = head;
        if (head != Null)
            SetPrev(head, bodyId, id);
        body.FirstContactEdge = id;
    }

    private void UnlinkEdge(int id, int bodyId, int previous, int next)
    {
        if (previous != Null)
            SetNext(previous, bodyId, next);
        else
            Bodies[bodyId].FirstContactEdge = next;
        if (next != Null)
            SetPrev(next, bodyId, previous);
    }

    private void SetPrev(int contactId, int bodyId, int value)
    {
        ref var contact = ref Contacts[contactId];
        if (contact.BodyA == bodyId)
            contact.PrevA = value;
        else
            contact.PrevB = value;
    }

    private void SetNext(int contactId, int bodyId, int value)
    {
        ref var contact = ref Contacts[contactId];
        if (contact.BodyA == bodyId)
            contact.NextA = value;
        else
            contact.NextB = value;
    }

    private void ComputeExtents(ref Body body)
    {
        var minExtent = float.MaxValue;
        var maxExtent = 0f;
        for (var f = body.FirstFixture; f != Null; f = Fixtures[f].Next)
        {
            ref var shape = ref Fixtures[f].Local;
            var centroid = shape.Centroid;
            var shapeMin = shape.Radius;
            if (shape.Type == ShapeType.Polygon)
            {
                shapeMin = float.MaxValue;
                for (var i = 0; i < shape.Count; i++)
                    shapeMin = MathF.Min(shapeMin, Vector2.Dot(shape.Normals[i], shape.Points[i] - centroid));
            }

            minExtent = MathF.Min(minExtent, shapeMin);
            for (var i = 0; i < shape.Count; i++)
                maxExtent = MathF.Max(maxExtent, Vector2.Distance(shape.Points[i], body.LocalCenter) + shape.Radius);
        }

        body.MinExtent = minExtent == float.MaxValue ? 0 : minExtent;
        body.MaxExtent = maxExtent;
    }

    private static int[] CreateFilled(int length)
    {
        var array = new int[length];
        Array.Fill(array, Null);
        return array;
    }
}
