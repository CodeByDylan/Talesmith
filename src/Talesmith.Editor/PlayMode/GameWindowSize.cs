using System.Globalization;

namespace Talesmith.Editor.PlayMode;

/// <summary>A window the game can run in: its size in device pixels and the scale of the display it is on, so 3840 × 2160 at 2× is a window of
/// 1920 × 1080 logical pixels.</summary>
public readonly record struct GameWindowSize(int Width, int Height, double DisplayScale = 1)
{
    public const int MinimumSide = 64;

    /// <summary>The largest side, that of an 8K display.</summary>
    public const int MaximumSide = 7680;

    private static readonly (double Ratio, string Name)[] Ratios =
    [
        (16 / 9.0, "16:9"), (16 / 10.0, "16:10"), (4 / 3.0, "4:3"), (3 / 2.0, "3:2"), (5 / 4.0, "5:4"), (1, "1:1"),
        (64 / 27.0, "21:9"), (32 / 9.0, "32:9"), (19.5 / 9, "19.5:9"), (20 / 9.0, "20:9")
    ];

    /// <summary>The display scales to choose from, as display settings offer them.</summary>
    public static IReadOnlyList<double> DisplayScales { get; } = [1, 1.25, 1.5, 1.75, 2, 2.5, 3];

    /// <summary>The window with each side between <see cref="MinimumSide"/> and <see cref="MaximumSide"/>, and a display scale from
    /// <see cref="DisplayScales"/>, or else 1.</summary>
    public GameWindowSize Clamped() =>
        new(Math.Clamp(Width, MinimumSide, MaximumSide), Math.Clamp(Height, MinimumSide, MaximumSide), DisplayScales.Contains(DisplayScale) ? DisplayScale : 1);

    /// <summary>The window turned a quarter: its width and height swapped.</summary>
    public GameWindowSize Rotated() => this with { Width = Height, Height = Width };

    /// <summary>The window's shape as displays name it, such as 16:9, or as a ratio to 1, such as 1.44:1; taller windows put the shorter side
    /// first, such as 9:16.</summary>
    public string AspectRatio
    {
        get
        {
            if (Width <= 0 || Height <= 0)
                return "";
            var ratio = (double)Math.Max(Width, Height) / Math.Min(Width, Height);
            var name = Ratios.FirstOrDefault(r => Math.Abs(ratio - r.Ratio) / r.Ratio < 0.01).Name
                       ?? ratio.ToString("0.##", CultureInfo.InvariantCulture) + ":1";
            if (Height <= Width)
                return name;
            var sides = name.Split(':');
            return $"{sides[1]}:{sides[0]}";
        }
    }
}
