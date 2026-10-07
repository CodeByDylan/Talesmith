using System.Numerics;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Shows entities with <see cref="SmoothMotion"/> between their last two simulated positions, by how far the game is into the next fixed step.</summary>
/// <remarks>
/// The gameplay runs at the fixed update rate of game.json, so it plays the same at any frame rate; this keeps it from looking choppy
/// when the display refreshes faster. Runs before the camera follows the hero.
/// </remarks>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateBefore(typeof(CameraFocusSystem))]
public sealed class SmoothMotionSystem : ISystem
{
    /// <summary>The farthest an entity can move in one fixed step and still be blended; anything farther, like a respawn, is a jump cut.</summary>
    public const float TeleportDistance = 64;

    public void Update(in SystemContext context)
    {
        var blend = context.Time.Interpolation;
        foreach (var archetype in context.World.Query<SmoothMotion, Transform>())
        {
            var motions = archetype.GetSpan<SmoothMotion>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < motions.Length; i++)
                transforms[i].Position = Vector2.Lerp(motions[i].Previous, motions[i].Current, blend);
        }
    }
}

/// <summary>Puts entities with <see cref="SmoothMotion"/> back at their simulated position before a fixed step moves them.</summary>
[UpdateIn(SystemPhase.FixedUpdate)]
[UpdateBefore(typeof(HeroMovementSystem))]
public sealed class SmoothMotionStepStartSystem : ISystem
{
    public void Update(in SystemContext context)
    {
        foreach (var archetype in context.World.Query<SmoothMotion, Transform>())
        {
            var motions = archetype.GetSpan<SmoothMotion>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < motions.Length; i++)
            {
                motions[i].Previous = motions[i].Current;
                transforms[i].Position = motions[i].Current;
            }
        }
    }
}

/// <summary>Records where a fixed step moved entities with <see cref="SmoothMotion"/>.</summary>
[UpdateIn(SystemPhase.FixedUpdate)]
[UpdateAfter(typeof(HeroMovementSystem))]
[UpdateAfter(typeof(CrabSystem))]
[UpdateAfter(typeof(PickupSystem))]
public sealed class SmoothMotionStepEndSystem : ISystem
{
    public void Update(in SystemContext context)
    {
        foreach (var archetype in context.World.Query<SmoothMotion, Transform>())
        {
            var motions = archetype.GetSpan<SmoothMotion>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < motions.Length; i++)
            {
                motions[i].Current = transforms[i].Position;
                if (Vector2.Distance(motions[i].Previous, motions[i].Current) > SmoothMotionSystem.TeleportDistance)
                    motions[i].Previous = motions[i].Current;
            }
        }
    }
}
