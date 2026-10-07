using Talesmith.Runtime.Systems;
using Talesmith.Systems;

namespace Talesmith.Physics.Systems;

/// <summary>Puts interpolated bodies back at their simulated pose at the start of each fixed step, before gameplay moves anything.</summary>
[UpdateIn(SystemPhase.FixedUpdate)]
[ExecuteIn(ExecutionModes.Play)]
[SystemOrder(PhysicsSystemOrder.Prepare)]
public sealed class PhysicsPrepareSystem(PhysicsWorld physics) : ISystem
{
    public void Update(in SystemContext context) => physics.BeginStep();
}

/// <summary>Steps the scene's physics once per fixed update, after gameplay systems of the fixed update set velocities and forces.</summary>
[UpdateIn(SystemPhase.FixedUpdate)]
[ExecuteIn(ExecutionModes.Play)]
[SystemOrder(PhysicsSystemOrder.Step)]
public sealed class PhysicsStepSystem(PhysicsWorld physics) : ISystem
{
    public void Update(in SystemContext context) => physics.Step(context.Time.DeltaTime);
}

/// <summary>Shows interpolated bodies between their last two fixed-step poses, before cameras follow them.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
[ExecuteIn(ExecutionModes.Play)]
[UpdateBefore(typeof(CameraSystem))]
[SystemOrder(PhysicsSystemOrder.Prepare)]
public sealed class PhysicsInterpolationSystem(PhysicsWorld physics) : ISystem
{
    public void Update(in SystemContext context) => physics.Interpolate(context.Time.Interpolation);
}

/// <summary>The <see cref="SystemOrderAttribute"/> values of the physics systems, for ordering game systems around them.</summary>
public static class PhysicsSystemOrder
{
    /// <summary>Runs first in the fixed update.</summary>
    public const int Prepare = -1000;

    /// <summary>Runs after fixed-update systems with the default order of 0.</summary>
    public const int Step = 1000;
}
