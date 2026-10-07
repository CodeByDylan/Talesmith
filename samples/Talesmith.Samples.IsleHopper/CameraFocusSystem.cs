using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Systems;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Moves the point the camera follows: above the hero, so the ground stays low on screen, and a little ahead of where it runs.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateBefore(typeof(CameraSystem))]
public sealed class CameraFocusSystem(Course course) : ISystem
{
    public const float Above = 75;
    public const float Ahead = 45;

    private float _lookAhead;

    public void Update(in SystemContext context)
    {
        var world = context.World;
        if (course.Level is null || !world.IsAlive(course.Hero) || !world.IsAlive(course.CameraFocus))
            return;

        var hero = world.Get<Hero>(course.Hero);
        var feet = world.Get<Transform>(course.Hero).Position;
        _lookAhead = MathHelper.Damp(_lookAhead, hero.FacingLeft ? -Ahead : Ahead, 2.5f, context.Time.DeltaTime);
        world.Get<Transform>(course.CameraFocus).Position = feet + new Vector2(_lookAhead, -Above);
    }
}
