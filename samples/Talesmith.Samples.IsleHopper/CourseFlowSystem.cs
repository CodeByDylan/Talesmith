using Talesmith.Input;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Keeps the run's clock and restarts the level on the "Restart" action, or on "Jump" once the lighthouse is reached.</summary>
[UpdateIn(SystemPhase.PreUpdate)]
public sealed class CourseFlowSystem(IInputService input, ISceneManager scenes, Course course, IsleHud hud) : ISystem
{
    /// <summary>How long the finish screen shows before a jump restarts the level, so a held jump does not skip it.</summary>
    public const double RestartDelaySeconds = 1;

    private double _completedAt = -1;
    private bool _restarting;

    public void Update(in SystemContext context)
    {
        if (course.Level is null || _restarting)
            return;

        if (course.IsRunning)
        {
            course.Elapsed += context.Time.DeltaTime;
            hud.Seconds = (int)course.Elapsed;
        }
        else if (_completedAt < 0)
        {
            _completedAt = context.Time.TotalTime;
        }

        var restart = input.Actions.TryGet("Restart", out var restartAction) && restartAction!.WasPressed;
        var replay = course.IsComplete && context.Time.TotalTime - _completedAt > RestartDelaySeconds
            && input.Actions.TryGet("Jump", out var jump) && jump!.WasPressed;
        if (!restart && !replay)
            return;

        _restarting = true;
        _ = scenes.ReloadAsync(SceneTransition.Default);
    }
}
