namespace Talesmith.Scripting;

/// <summary>Timing for scripts; see <see cref="Script.Time"/>.</summary>
public sealed class ScriptTime
{
    private readonly ScriptRuntime _runtime;

    internal ScriptTime(ScriptRuntime runtime) => _runtime = runtime;

    /// <summary>Seconds covered by the current update, scaled by <see cref="TimeScale"/>; in <see cref="Script.FixedUpdate"/> it is the fixed step.</summary>
    public float DeltaTime => _runtime.Time.DeltaTime;

    /// <summary>The length of a fixed step in seconds.</summary>
    public float FixedDeltaTime => _runtime.FixedDeltaTime;

    /// <summary>Scaled seconds since the game started; in <see cref="Script.FixedUpdate"/>, the time of the fixed step.</summary>
    public double TotalTime => _runtime.Time.TotalTime;

    /// <summary>Real seconds since the previous frame, ignoring the time scale and pauses.</summary>
    public float UnscaledDeltaTime => _runtime.Time.UnscaledDeltaTime;

    /// <summary>Real seconds since the game started.</summary>
    public double UnscaledTotalTime => _runtime.Time.UnscaledTotalTime;

    /// <summary>Frames since the game started.</summary>
    public long FrameCount => _runtime.Time.FrameCount;

    /// <summary>Multiplies game time: 0.5 is slow motion, 0 stops it. Unscaled time is unaffected.</summary>
    public float TimeScale
    {
        get => _runtime.Game?.TimeScale ?? 1;
        set
        {
            if (_runtime.Game is { } game)
                game.TimeScale = value;
        }
    }

    /// <summary>Whether game time is paused; input, rendering and real-time waits keep running.</summary>
    public bool IsPaused
    {
        get => _runtime.Game?.IsPaused ?? false;
        set
        {
            if (_runtime.Game is { } game)
                game.IsPaused = value;
        }
    }
}
