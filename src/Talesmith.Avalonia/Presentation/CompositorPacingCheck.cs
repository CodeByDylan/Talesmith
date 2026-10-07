using System.Diagnostics;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Tells whether the window's animation frames follow the display, by comparing how often they arrive with its refresh rate.</summary>
/// <remarks>The rate is the median interval between frames after a warm-up, so loading and occasional hitches do not skew it.</remarks>
/// <param name="displayRate">Gets the refresh rate of the window's monitor in hertz, or null when it is unknown.</param>
internal sealed class CompositorPacingCheck(Func<double?> displayRate)
{
    /// <summary>Animation frames ignored at first, while the game is still loading and settling.</summary>
    public const int WarmupFrames = 60;

    /// <summary>The intervals between animation frames measured before deciding, under two seconds at common refresh rates.</summary>
    public const int Frames = 240;

    /// <summary>How much faster than the display animation frames may arrive; the median of <see cref="Frames"/> intervals varies far less.</summary>
    public const double Tolerance = 1.015;

    private readonly long[] _intervals = new long[Frames];
    private int _seen;
    private int _count;
    private long _previous;

    public bool IsComplete { get; private set; }

    /// <summary>Animation frames per second, once the check is complete.</summary>
    public double FrameRate { get; private set; }

    /// <summary>The display's refresh rate, once the check is complete, or null when it is unknown.</summary>
    public double? DisplayRate { get; private set; }

    /// <summary>Records an animation frame at a <see cref="Stopwatch"/> timestamp.</summary>
    /// <returns>True once, when enough frames arrived to tell that they come faster than the display refreshes.</returns>
    public bool Observe(long timestamp)
    {
        if (IsComplete)
            return false;

        var previous = _previous;
        _previous = timestamp;
        if (++_seen <= WarmupFrames)
            return false;

        _intervals[_count++] = timestamp - previous;
        if (_count < Frames)
            return false;

        IsComplete = true;
        Array.Sort(_intervals);
        var median = (_intervals[Frames / 2 - 1] + _intervals[Frames / 2]) / 2.0;
        FrameRate = median > 0 ? Stopwatch.Frequency / median : double.PositiveInfinity;
        DisplayRate = displayRate();
        return DisplayRate is { } rate && FrameRate > rate * Tolerance;
    }
}
