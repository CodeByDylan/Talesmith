namespace Talesmith.Time;

/// <summary>Accumulates real time and reports how many fixed simulation steps are due.</summary>
/// <remarks>
/// The number of steps per frame is capped so a slow frame cannot trigger an ever-growing backlog; time beyond the cap is dropped
/// and counted in <see cref="DroppedSeconds"/>.
/// </remarks>
public sealed class FixedTimestep
{
    private double _accumulator;

    public FixedTimestep(double stepSeconds = 1.0 / 60.0, int maxStepsPerFrame = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stepSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStepsPerFrame);
        StepSeconds = stepSeconds;
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    public double StepSeconds { get; }

    public int MaxStepsPerFrame { get; }

    /// <summary>Scaled seconds that were discarded because a frame needed more steps than allowed.</summary>
    public double DroppedSeconds { get; private set; }

    /// <summary>How far the accumulated time is into the next step, from 0 to 1.</summary>
    public float Interpolation => (float)(_accumulator / StepSeconds);

    /// <summary>Adds scaled elapsed time and returns the number of fixed steps to run now.</summary>
    public int Advance(double scaledSeconds)
    {
        _accumulator += Math.Max(0, scaledSeconds);
        var steps = (int)(_accumulator / StepSeconds);
        if (steps > MaxStepsPerFrame)
        {
            DroppedSeconds += (steps - MaxStepsPerFrame) * StepSeconds;
            _accumulator -= (steps - MaxStepsPerFrame) * StepSeconds;
            steps = MaxStepsPerFrame;
        }

        _accumulator -= steps * StepSeconds;
        return steps;
    }

    public void Reset() => _accumulator = 0;
}
