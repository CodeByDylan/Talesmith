using Avalonia.Media;

namespace Talesmith.Editor.Particles;

/// <summary>Stroke icons for effects and lights that the shared icon set does not have, on the same 24×24 grid.</summary>
public static class ParticleIcons
{
    public static Geometry Wind { get; } = Parse("M17.7 7.7a2.5 2.5 0 1 1 1.8 4.3H2 M9.6 4.6A2 2 0 1 1 11 8H2 M12.6 19.4A2 2 0 1 0 14 16H2");

    public static Geometry Flame { get; } = Parse(
        "M8.5 14.5A2.5 2.5 0 0 0 11 12c0-1.38-.5-2-1-3-1.072-2.143-.224-4.054 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 1 1-14 0c0-1.153.433-2.294 1-3a2.5 2.5 0 0 0 2.5 2.5z");

    public static Geometry Cloud { get; } = Parse("M17.5 19H9a7 7 0 1 1 6.71-9h1.79a4.5 4.5 0 1 1 0 9Z");

    public static Geometry Zap { get; } = Parse(
        "M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z");

    public static Geometry Rain { get; } = Parse("M4 14.9A7 7 0 1 1 15.71 8h1.79a4.5 4.5 0 0 1 2.5 8.24 M16 14v6 M8 14v6 M12 16v6");

    public static Geometry Snowflake { get; } = Parse("M2 12h20 M12 2v20 M20 16l-4-4 4-4 M4 8l4 4-4 4 M16 4l-4 4-4-4 M8 20l4-4 4 4");

    public static Geometry Burst { get; } = Parse(
        "M12 2v4 M12 18v4 M4.93 4.93l2.83 2.83 M16.24 16.24l2.83 2.83 M2 12h4 M18 12h4 M4.93 19.07l2.83-2.83 M16.24 7.76l2.83-2.83");

    /// <summary>A half-filled circle, for shadows.</summary>
    public static Geometry Contrast { get; } = Parse("M22 12a10 10 0 1 1-20 0 10 10 0 0 1 20 0 M12 18a6 6 0 0 0 0-12v12z");

    /// <summary>A light bulb with rays, for glowing sprites.</summary>
    public static Geometry Glow { get; } = Parse(
        "M12 8a4 4 0 1 1 0 8 4 4 0 0 1 0-8 M12 2v2 M12 20v2 M4.93 4.93l1.41 1.41 M17.66 17.66l1.41 1.41 M2 12h2 M20 12h2 M6.34 17.66l-1.41 1.41 M19.07 4.93l-1.41 1.41");

    /// <summary>The icon of a built-in preset, by name.</summary>
    public static Geometry ForPreset(string name) => name.ToUpperInvariant() switch
    {
        "FIRE" => Flame,
        "SMOKE" or "DUST PUFF" => Cloud,
        "SPARKS" => Zap,
        "RAIN" => Rain,
        "SNOW" => Snowflake,
        "EXPLOSION" => Burst,
        _ => UI.Icons.Sparkles
    };

    private static Geometry Parse(string data) => Geometry.Parse(data);
}
