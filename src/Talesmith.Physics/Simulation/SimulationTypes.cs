using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Physics.Collision;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Simulation;

internal enum BodyKind : byte
{
    Static,
    Kinematic,
    Dynamic
}

[Flags]
internal enum BodyFlags : ushort
{
    None = 0,
    Alive = 1,
    Awake = 1 << 1,
    FixedRotation = 1 << 2,
    Bullet = 1 << 3,
    Interpolate = 1 << 4,
    CanSleep = 1 << 5,

    /// <summary>A kinematic body moved by <see cref="CharacterController2D"/> instead of by its velocity.</summary>
    Character = 1 << 6,

    /// <summary>A static body holding the collision of one tile map chunk.</summary>
    TileChunk = 1 << 7,

    /// <summary>Moved outside the solver this step, such as by a teleport or a character move.</summary>
    Moved = 1 << 8,

    HasMoveTarget = 1 << 9
}

/// <summary>What a body's collider was built from, to detect edits.</summary>
internal struct ColliderSignature : IEquatable<ColliderSignature>
{
    public bool HasCollider;
    public ColliderShape Shape;
    public Vector2 Size;
    public float Radius;
    public Vector2[]? Points;
    public int PointsHash;
    public Vector2 Offset;
    public float Rotation;
    public bool IsTrigger;
    public bool OneWay;
    public float Friction;
    public float Restitution;
    public float Density;
    public int Layer;
    public int Mask;
    public Vector2 Scale;

    public static ColliderSignature From(in Collider2D collider, Vector2 scale) => new()
    {
        HasCollider = true,
        Shape = collider.Shape,
        Size = collider.Size,
        Radius = collider.Radius,
        Points = collider.Points,
        PointsHash = Hash(collider.Points),
        Offset = collider.Offset,
        Rotation = collider.Rotation,
        IsTrigger = collider.IsTrigger,
        OneWay = collider.OneWay,
        Friction = collider.Friction,
        Restitution = collider.Restitution,
        Density = collider.Density,
        Layer = collider.Layer,
        Mask = collider.CollisionMask,
        Scale = scale
    };

    public static int Hash(Vector2[]? points)
    {
        if (points is null)
            return 0;
        var hash = new HashCode();
        foreach (var point in points)
            hash.Add(point);
        return hash.ToHashCode();
    }

    public readonly bool Equals(ColliderSignature other) =>
        HasCollider == other.HasCollider && Shape == other.Shape && Size == other.Size && Radius == other.Radius
        && ReferenceEquals(Points, other.Points) && PointsHash == other.PointsHash && Offset == other.Offset && Rotation == other.Rotation
        && IsTrigger == other.IsTrigger && OneWay == other.OneWay && Friction == other.Friction && Restitution == other.Restitution
        && Density == other.Density && Layer == other.Layer && Mask == other.Mask && Scale == other.Scale;

    public override readonly bool Equals(object? obj) => obj is ColliderSignature other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Shape, Size, Radius, PointsHash, Offset, Rotation, Layer, Scale);
}

/// <summary>A simulated body: its pose, motion, mass and the list of its fixtures and contacts.</summary>
internal struct Body
{
    public Entity Entity;
    public BodyKind Kind;
    public BodyFlags Flags;

    /// <summary>The body's origin, which is the entity's transform position.</summary>
    public Vector2 Origin;
    public float Angle;
    public Rot Q;

    /// <summary>The center of mass relative to the origin, in body space.</summary>
    public Vector2 LocalCenter;

    /// <summary>The center of mass in world space.</summary>
    public Vector2 Center;

    /// <summary>The center of mass and angle when the solver started this step.</summary>
    public Vector2 Center0;
    public float Angle0;

    /// <summary>The pose before the last fixed step, for interpolation.</summary>
    public Vector2 PreviousOrigin;
    public float PreviousAngle;

    public Vector2 Velocity;
    public float AngularVelocity;
    public Vector2 Force;
    public float Torque;

    public float Mass;
    public float InvMass;
    public float Inertia;
    public float InvInertia;
    public float GravityScale;
    public float LinearDrag;
    public float AngularDrag;
    public float SleepTime;
    public float MinExtent;
    public float MaxExtent;

    public int FirstFixture;
    public int FirstContactEdge;
    public int SyncStamp;
    public int NextFree;

    /// <summary>The transform and velocity last written to or read from the entity, to notice changes made by game code.</summary>
    public Vector2 WrittenPosition;
    public float WrittenRotation;
    public Vector2 WrittenScale;
    public Vector2 WrittenVelocity;
    public float WrittenAngularVelocity;

    public Vector2 MoveTarget;
    public float MoveTargetAngle;
    public ColliderSignature Collider;
    public float RigidbodyMass;
    public bool RigidbodyAutoMass;

    public readonly bool IsAlive => (Flags & BodyFlags.Alive) != 0;

    public readonly bool IsAwake => (Flags & BodyFlags.Awake) != 0;

    public readonly bool Has(BodyFlags flag) => (Flags & flag) != 0;

    public void Set(BodyFlags flag, bool value) => Flags = value ? Flags | flag : Flags & ~flag;

    public readonly Xf Transform => new(Origin, Q);

    /// <summary>Recomputes the origin from the center of mass after the solver moved the body.</summary>
    public void SyncOriginFromCenter()
    {
        Q = Rot.FromAngle(Angle);
        Origin = Center - Q.Rotate(LocalCenter);
    }

    public void SetPose(Vector2 origin, float angle)
    {
        Origin = origin;
        Angle = angle;
        Q = Rot.FromAngle(angle);
        Center = origin + Q.Rotate(LocalCenter);
    }
}

/// <summary>One convex shape attached to a body, with its material, filter and broadphase proxy.</summary>
internal struct Fixture
{
    public int Body;
    public int Next;
    public Shape Local;
    public Shape World;
    public Aabb Aabb;
    public int Proxy;
    public float Friction;
    public float Restitution;
    public float Density;
    public int Layer;
    public int Mask;
    public bool IsTrigger;
    public bool OneWay;
    public bool Alive;

    /// <summary>The up direction of a one-way fixture in body space.</summary>
    public Vector2 LocalUp;

    /// <summary>Links the fixtures found by the query in progress.</summary>
    public int QueryNext;
}

/// <summary>The material and filter of a fixture to create.</summary>
internal readonly record struct FixtureOptions(float Friction, float Restitution, float Density, int Layer, int Mask, bool IsTrigger, bool OneWay, Vector2 LocalUp);

[Flags]
internal enum ContactFlags : byte
{
    None = 0,
    Touching = 1,
    Sensor = 2,

    /// <summary>A one-way fixture lets the other shape pass through until they separate.</summary>
    Disabled = 4,

    /// <summary>The shapes touch closely enough to report the contact through events.</summary>
    Reported = 8
}

/// <summary>A pair of fixtures whose fat bounds overlap, with their manifold.</summary>
internal struct Contact
{
    public int FixtureA;
    public int FixtureB;
    public int BodyA;
    public int BodyB;
    public ContactFlags Flags;
    public Manifold Manifold;
    public float Friction;
    public float Restitution;

    /// <summary>Links of the contact lists of body A and body B.</summary>
    public int PrevA;
    public int NextA;
    public int PrevB;
    public int NextB;

    /// <summary>The position in the list of active contacts.</summary>
    public int ActiveIndex;
    public int NextFree;
    public bool Alive;

    public readonly bool IsTouching => (Flags & ContactFlags.Touching) != 0;

    public readonly bool IsSensor => (Flags & ContactFlags.Sensor) != 0;

    public readonly bool IsDisabled => (Flags & ContactFlags.Disabled) != 0;

    public readonly bool IsReported => (Flags & ContactFlags.Reported) != 0;

    /// <summary>Whether the solver should keep the two shapes apart this step.</summary>
    public readonly bool IsSolid => (Flags & (ContactFlags.Touching | ContactFlags.Sensor | ContactFlags.Disabled)) == ContactFlags.Touching;

    public readonly int Other(int body) => BodyA == body ? BodyB : BodyA;

    public readonly int NextOf(int body) => BodyA == body ? NextA : NextB;
}

internal enum ContactEventKind : byte
{
    CollisionEntered,
    CollisionStayed,
    CollisionExited,
    TriggerEntered,
    TriggerStayed,
    TriggerExited
}

/// <summary>A contact event recorded during a step and delivered after it.</summary>
/// <param name="Contact">The contact whose impulse completes the data after solving, or -1 when it no longer exists.</param>
internal record struct ContactEvent(ContactEventKind Kind, int Contact, PhysicsContact Data);
