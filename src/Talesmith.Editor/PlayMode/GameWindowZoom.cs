using System.Globalization;

namespace Talesmith.Editor.PlayMode;

/// <summary>How large the Game panel shows a previewed window: as large as fits, up to its actual size, or at a factor of its pixels.</summary>
/// <param name="Factor">Screen pixels per window pixel, or null to fit.</param>
public sealed record GameWindowZoom(double? Factor)
{
    public static GameWindowZoom Fit { get; } = new((double?)null);

    public static IReadOnlyList<GameWindowZoom> All { get; } =
        [Fit, new(0.25), new(0.5), new(0.75), new(1), new(1.5), new(2), new(4)];

    public string Name => Factor is { } factor ? Percent(factor) : "Fit";

    /// <summary>A zoom as a percentage, such as "75%".</summary>
    public static string Percent(double factor) => string.Create(CultureInfo.InvariantCulture, $"{factor * 100:0}%");
}
