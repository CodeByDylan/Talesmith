namespace Talesmith.Time;

/// <summary>Timing information for one update.</summary>
/// <param name="DeltaTime">Seconds covered by this update, scaled by the time scale. Fixed updates always see the fixed step.</param>
/// <param name="TotalTime">Scaled seconds since the game started.</param>
/// <param name="UnscaledDeltaTime">Real seconds since the previous frame, ignoring the time scale and pauses.</param>
/// <param name="UnscaledTotalTime">Real seconds since the game started.</param>
/// <param name="FrameCount">Rendered frames since the game started.</param>
/// <param name="Interpolation">How far rendering is between the last two fixed updates, from 0 to 1.</param>
public readonly record struct GameTime(
    float DeltaTime,
    double TotalTime,
    float UnscaledDeltaTime,
    double UnscaledTotalTime,
    long FrameCount,
    float Interpolation)
{
    public static GameTime Zero => default;
}
