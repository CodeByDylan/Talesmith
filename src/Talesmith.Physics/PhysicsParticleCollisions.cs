using Talesmith.VFX;

namespace Talesmith.Physics;

/// <summary>Collides particles of emitters with <see cref="CollisionModule.WorldColliders"/> on with the scene's solid colliders and collision tiles.</summary>
/// <remarks>Each live particle sweeps a circle of its collision radius along this step's movement; triggers are ignored.</remarks>
internal sealed class PhysicsParticleCollisions(IPhysicsWorld physics) : IParticleCollisionProvider
{
    private const float MinMotion = 1e-4f;

    public void Collide(ParticleCollisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var filter = new QueryFilter(context.LayerMask);
        for (var i = 0; i < context.Count; i++)
        {
            if (!context.IsAlive(i))
                continue;
            var from = context.PreviousPosition(i);
            var motion = context.Position(i) - from;
            var distance = motion.Length();
            if (distance < MinMotion)
                continue;
            var radius = context.Radius(i);
            var hit = radius > 0
                ? physics.ShapeCast(QueryShape.Circle(radius), from, 0, motion, distance, filter, out var result)
                : physics.RayCast(from, motion, distance, filter, out result);
            if (hit)
                context.Hit(i, result.Point, result.Normal);
        }
    }
}
