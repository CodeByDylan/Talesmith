using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>A named starting point offered by the curve editor.</summary>
public sealed record CurvePreset(string Name, Curve Curve);

/// <summary>The curve shapes offered by the curve editor, all from 0 to 1 over time 0 to 1.</summary>
public static class CurvePresets
{
    public static Curve Constant { get; } = Curve.Constant(1);

    public static Curve LinearUp { get; } = Curve.Linear(0, 1);

    public static Curve LinearDown { get; } = Curve.Linear(1, 0);

    public static Curve EaseIn { get; } = new([new CurveKey(0, 0), new CurveKey(1, 1, 2, 2)]);

    public static Curve EaseOut { get; } = new([new CurveKey(0, 0, 2, 2), new CurveKey(1, 1)]);

    public static Curve EaseInOut { get; } = new([new CurveKey(0, 0), new CurveKey(1, 1)]);

    public static Curve Bell { get; } = new([new CurveKey(0, 0), new CurveKey(0.5f, 1), new CurveKey(1, 0)]);

    /// <summary>Gets every preset in display order.</summary>
    public static IReadOnlyList<CurvePreset> All { get; } =
    [
        new("Constant", Constant),
        new("Linear up", LinearUp),
        new("Linear down", LinearDown),
        new("Ease in", EaseIn),
        new("Ease out", EaseOut),
        new("Ease in and out", EaseInOut),
        new("Bell", Bell),
    ];
}
