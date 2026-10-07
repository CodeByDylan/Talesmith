using System.Numerics;
using Talesmith.Ecs;

namespace Talesmith.VFX;

/// <summary>Collides particles with world geometry, such as physics colliders or collision tiles.</summary>
/// <remarks>
/// Register implementations as singletons, or scoped to use a scene's services; emitters whose <see cref="CollisionModule.WorldColliders"/> is on call every provider once
/// per simulation step with all of their particles. Report each hit with <see cref="ParticleCollisionContext.Hit"/>, which applies the
/// emitter's bounce, friction, lifetime loss and events.
/// </remarks>
public interface IParticleCollisionProvider
{
    void Collide(ParticleCollisionContext context);
}

/// <summary>A particle hitting a surface, published on the event bus when <see cref="CollisionModule.SendEvents"/> is on.</summary>
/// <param name="Emitter">The emitter's entity.</param>
/// <param name="Position">Where the particle hit, in world space.</param>
/// <param name="Normal">The surface normal at the hit, pointing away from the surface.</param>
/// <param name="Velocity">The particle's world velocity just before the hit.</param>
public readonly record struct ParticleCollision(Entity Emitter, Vector2 Position, Vector2 Normal, Vector2 Velocity);

/// <summary>The particles of one emitter after a step's movement, in world space, for <see cref="IParticleCollisionProvider"/>s.</summary>
public sealed class ParticleCollisionContext
{
    private readonly ParticleBuffer _particles;
    private ParticleCollision[] _events = new ParticleCollision[16];
    private Matrix3x2 _toWorld = Matrix3x2.Identity;
    private Matrix3x2 _toLocal = Matrix3x2.Identity;
    private bool _local;
    private CollisionModule _module = new();
    private float _deltaTime;

    internal ParticleCollisionContext(ParticleBuffer particles)
    {
        _particles = particles;
    }

    /// <summary>The number of particles; indices go from 0 to <see cref="Count"/> - 1.</summary>
    public int Count => _particles.Count;

    /// <summary>The collision layers to collide with, as a bit mask.</summary>
    public int LayerMask => _module.LayerMask;

    public Entity Emitter { get; private set; }

    internal int EventCount { get; private set; }

    internal ReadOnlySpan<ParticleCollision> Events => _events.AsSpan(0, EventCount);

    internal int MaxEvents { get; set; } = 16;

    /// <summary>Whether a particle is still alive; particles killed by an earlier hit this step are skipped.</summary>
    public bool IsAlive(int index) => _particles.AgeArray[index] < 1;

    /// <summary>The particle's world position after this step's movement.</summary>
    public Vector2 Position(int index)
    {
        var position = new Vector2(_particles.PositionXArray[index], _particles.PositionYArray[index]);
        return _local ? Vector2.Transform(position, _toWorld) : position;
    }

    /// <summary>The particle's world position before this step's movement; the particle moved in a straight line from here.</summary>
    public Vector2 PreviousPosition(int index) => Position(index) - Velocity(index) * _deltaTime;

    public Vector2 Velocity(int index)
    {
        var velocity = new Vector2(_particles.VelocityXArray[index], _particles.VelocityYArray[index]);
        return _local ? Vector2.TransformNormal(velocity, _toWorld) : velocity;
    }

    /// <summary>The particle's collision radius in world units.</summary>
    public float Radius(int index) => _particles.SizeArray[index] * _module.RadiusScale;

    /// <summary>Resolves a hit: moves the particle out of the surface and bounces, slows, ages or kills it as the emitter's settings say.</summary>
    /// <param name="point">The contact point in world space.</param>
    /// <param name="normal">The unit surface normal in world space, pointing toward the side the particle came from.</param>
    public void Hit(int index, Vector2 point, Vector2 normal)
    {
        if (!IsAlive(index))
            return;
        var velocity = Velocity(index);
        if (_module.SendEvents && EventCount < MaxEvents)
        {
            if (EventCount == _events.Length)
                Array.Resize(ref _events, _events.Length * 2);
            _events[EventCount++] = new ParticleCollision(Emitter, point, normal, velocity);
        }

        if (_module.KillOnCollision)
        {
            _particles.Kill(index);
            return;
        }

        var normalSpeed = Vector2.Dot(velocity, normal);
        var tangent = velocity - normal * normalSpeed;
        if (normalSpeed < 0)
            velocity = tangent * (1 - _module.Friction) - normal * (normalSpeed * _module.Bounce);
        var position = point + normal * Radius(index);
        if (_local)
        {
            position = Vector2.Transform(position, _toLocal);
            velocity = Vector2.TransformNormal(velocity, _toLocal);
        }

        _particles.PositionXArray[index] = position.X;
        _particles.PositionYArray[index] = position.Y;
        _particles.VelocityXArray[index] = velocity.X;
        _particles.VelocityYArray[index] = velocity.Y;
        if (_module.LifetimeLoss > 0)
            _particles.AgeArray[index] = MathF.Min(1, _particles.AgeArray[index] + _module.LifetimeLoss);
    }

    internal void Begin(CollisionModule module, Entity emitter, float deltaTime, bool local, in Matrix3x2 toWorld)
    {
        _module = module;
        Emitter = emitter;
        _deltaTime = deltaTime;
        _local = local;
        _toWorld = toWorld;
        if (!local || !Matrix3x2.Invert(toWorld, out _toLocal))
            _toLocal = Matrix3x2.Identity;
    }

    internal void ClearEvents() => EventCount = 0;
}

/// <summary>The built-in infinite ground line of <see cref="CollisionModule"/>.</summary>
internal static class GroundPlaneCollision
{
    public static void Collide(ParticleCollisionContext context, CollisionModule module, Vector2 emitterPosition)
    {
        var (sin, cos) = MathF.SinCos(module.GroundAngle);
        var normal = new Vector2(sin, -cos);
        var origin = emitterPosition + new Vector2(-normal.X, -normal.Y) * module.GroundOffset;
        for (var i = 0; i < context.Count; i++)
        {
            if (!context.IsAlive(i))
                continue;
            var position = context.Position(i);
            var distance = Vector2.Dot(position - origin, normal) - context.Radius(i);
            if (distance >= 0)
                continue;
            context.Hit(i, position - normal * (distance + context.Radius(i)), normal);
        }
    }
}
