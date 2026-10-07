using Talesmith.Input;
using Talesmith.Runtime.Hosting;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Reads the "Run" and "Jump" actions every frame into the hero's <see cref="HeroControls"/>, for the fixed-step movement to use.</summary>
/// <remarks>The hero ignores the player once the level is complete and while something, such as a cutscene, suspends player control.</remarks>
[UpdateIn(SystemPhase.PreUpdate)]
[UpdateAfter(typeof(HeroSpawnSystem))]
public sealed class HeroInputSystem(IInputService input, PlayerControl control, Course course) : ISystem
{
    public void Update(in SystemContext context)
    {
        if (course.Level is null || !context.World.IsAlive(course.Hero))
            return;

        ref var controls = ref context.World.Get<HeroControls>(course.Hero);
        if (!course.IsRunning || control.IsSuspended)
        {
            controls = default;
            return;
        }

        controls.Run = input.Actions.TryGet("Run", out var run) ? Math.Clamp(run!.Value, -1, 1) : 0;
        var jump = input.Actions.TryGet("Jump", out var action) ? action : null;
        controls.JumpHeld = jump is { IsDown: true };
        controls.JumpPressed |= jump is { WasPressed: true };
    }
}
