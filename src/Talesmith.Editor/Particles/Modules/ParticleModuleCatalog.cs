using System.Globalization;
using Avalonia.Media;
using Talesmith.Mathematics;
using Talesmith.UI;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles.Modules;

/// <summary>Which part of an effect a module shapes; cards are tinted by it.</summary>
public enum ModuleCategory
{
    Playback,
    Spawn,
    Motion,
    Look,
    Collision,
    Plugin
}

/// <summary>How the editor shows a built-in module: its icon, category, one-line summary and which fields apply.</summary>
/// <param name="Key">The module's property in the settings, such as "emission"; empty for the playback settings at the root.</param>
public sealed record ParticleModuleInfo(string Key, string Title, Geometry Icon, ModuleCategory Category, Func<ParticleSettings, string> Summary)
{
    /// <summary>Whether a field of the module applies with the current settings; fields that do not are hidden.</summary>
    public Func<ParticleSettings, string, bool> IsRelevant { get; init; } = static (_, _) => true;

    /// <summary>A curve shown next to the summary, such as the size over lifetime.</summary>
    public Func<ParticleSettings, Curve?>? SummaryCurve { get; init; }

    public Func<ParticleSettings, Gradient?>? SummaryGradient { get; init; }
}

/// <summary>The built-in modules in the order the editor shows them.</summary>
public static class ParticleModuleCatalog
{
    public const string PlaybackKey = "";

    /// <summary>The root settings shown in the playback card.</summary>
    public static IReadOnlyList<string> PlaybackFields { get; } =
        ["duration", "looping", "prewarm", "playOnStart", "startDelay", "simulationSpeed", "simulationSpace", "maxParticles", "seed", "culling"];

    public static IReadOnlyList<ParticleModuleInfo> All { get; } =
    [
        new(PlaybackKey, "Playback", Icons.Play, ModuleCategory.Playback, s =>
            $"{Seconds(s.Duration)}{(s.Looping ? " · loop" : " · once")}{(s.Prewarm ? " · prewarm" : "")} · {s.MaxParticles:N0} max"),
        new("emission", "Emission", Icons.Sparkles, ModuleCategory.Spawn, s =>
            Join(s.Emission.RateOverTime > 0 ? $"{Number(s.Emission.RateOverTime)}/s" : null,
                s.Emission.RateOverDistance > 0 ? $"{Number(s.Emission.RateOverDistance)}/unit" : null,
                s.Emission.Bursts.Count switch { 0 => null, 1 => "1 burst", var n => $"{n} bursts" }) ?? "Nothing emitted"),
        new("shape", "Shape", Icons.Hexagon, ModuleCategory.Spawn, ShapeSummary) { IsRelevant = ShapeFieldApplies },
        new("initial", "Initial values", Icons.Sliders, ModuleCategory.Spawn, s =>
            $"life {Range(s.Initial.Lifetime)} s · speed {Range(s.Initial.Speed)} · size {Range(s.Initial.Size)}"),
        new("velocityOverLifetime", "Velocity over lifetime", Icons.Move, ModuleCategory.Motion, s =>
            Join(s.VelocityOverLifetime.Linear != default ? $"drift ({Number(s.VelocityOverLifetime.Linear.X)}, {Number(s.VelocityOverLifetime.Linear.Y)})" : null,
                s.VelocityOverLifetime.Orbital != 0 ? $"orbit {Degrees(s.VelocityOverLifetime.Orbital)}/s" : null,
                s.VelocityOverLifetime.Radial != 0 ? $"radial {Number(s.VelocityOverLifetime.Radial)}" : null) ?? "No extra movement"),
        new("forces", "Forces and gravity", Icons.Weight, ModuleCategory.Motion, s =>
            Join($"gravity ×{Number(s.Forces.GravityScale)}",
                s.Forces.Acceleration != default ? $"push ({Number(s.Forces.Acceleration.X)}, {Number(s.Forces.Acceleration.Y)})" : null)!),
        new("drag", "Drag", ParticleIcons.Wind, ModuleCategory.Motion, s => $"{Number(s.Drag.Drag)} per second"),
        new("noise", "Noise", Icons.Activity, ModuleCategory.Motion, s => $"strength {Number(s.Noise.Strength)} · swirls {Number(s.Noise.Scale)}"),
        new("colorOverLifetime", "Color over lifetime", Icons.Palette, ModuleCategory.Look, s => $"{s.ColorOverLifetime.Color.Stops.Length} stops")
        {
            SummaryGradient = s => s.ColorOverLifetime.Color
        },
        new("sizeOverLifetime", "Size over lifetime", Icons.Maximize2, ModuleCategory.Look, s => CurveSummary(s.SizeOverLifetime.Size))
        {
            SummaryCurve = s => s.SizeOverLifetime.Size
        },
        new("rotationOverLifetime", "Rotation over lifetime", Icons.RotateCw, ModuleCategory.Look, s => $"{Degrees(s.RotationOverLifetime.AngularVelocity)}/s")
        {
            SummaryCurve = s => s.RotationOverLifetime.OverLifetime
        },
        new("textureSheet", "Texture sheet", Icons.Grid, ModuleCategory.Look, s =>
            $"{s.TextureSheet.Columns}×{s.TextureSheet.Rows} · {(s.TextureSheet.Mode == TextureSheetMode.OverLifetime ? "over lifetime" : $"{Number(s.TextureSheet.FramesPerSecond)} fps")}")
        {
            IsRelevant = static (s, field) => field switch
            {
                "framesPerSecond" => s.TextureSheet.Mode == TextureSheetMode.FramesPerSecond,
                "cycles" => s.TextureSheet.Mode == TextureSheetMode.OverLifetime,
                _ => true
            }
        },
        new("collision", "Collision", Icons.Shield, ModuleCategory.Collision, s =>
            Join(s.Collision.GroundPlane ? "ground" : null, s.Collision.WorldColliders ? "world" : null, $"bounce {Number(s.Collision.Bounce)}",
                s.Collision.KillOnCollision ? "kill" : null)!)
        {
            IsRelevant = static (s, field) => field switch
            {
                "groundOffset" or "groundAngle" => s.Collision.GroundPlane,
                "layerMask" => s.Collision.WorldColliders,
                _ => true
            }
        },
        new("renderer", "Renderer", Icons.Image, ModuleCategory.Look, s =>
            $"{(s.Renderer.Texture.IsEmpty ? Humanize(s.Renderer.BuiltInTexture.ToString()) : "texture")} · {s.Renderer.Blend.ToString().ToLowerInvariant()} · {s.Renderer.Alignment.ToString().ToLowerInvariant()}")
        {
            IsRelevant = static (s, field) => field switch
            {
                "sprite" => !s.Renderer.Texture.IsEmpty,
                "builtInTexture" => s.Renderer.Texture.IsEmpty,
                "stretchSpeedScale" or "stretchLengthScale" => s.Renderer.Alignment == ParticleAlignment.Stretch,
                _ => true
            }
        }
    ];

    public static ParticleModuleInfo? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>Card details for a plugin module.</summary>
    public static ParticleModuleInfo Plugin(string title) => new("", title, Icons.Puzzle, ModuleCategory.Plugin, _ => "Plugin module");

    private static string ShapeSummary(ParticleSettings settings)
    {
        var shape = settings.Shape;
        var size = shape.Kind switch
        {
            ParticleShapeKind.Circle => shape.InnerRadius > 0 ? $"ring {Number(shape.InnerRadius)}–{Number(shape.Radius)}" : $"radius {Number(shape.Radius)}",
            ParticleShapeKind.Cone => $"{Degrees(shape.ConeAngle)} cone",
            ParticleShapeKind.Rectangle => $"{Number(shape.Size.X)}×{Number(shape.Size.Y)}",
            ParticleShapeKind.Line => $"{Number(shape.Size.X)} long",
            ParticleShapeKind.Tile => Humanize(shape.Tile.ToString()).ToLowerInvariant(),
            ParticleShapeKind.Polygon => $"{shape.Points.Count} points",
            _ => null
        };
        var direction = shape.Direction switch
        {
            ParticleDirectionMode.Fixed => $"toward {Degrees(shape.Angle)}",
            ParticleDirectionMode.Random => "any direction",
            _ => "outward"
        };
        return Join(Humanize(shape.Kind.ToString()), size, direction)!;
    }

    private static bool ShapeFieldApplies(ParticleSettings settings, string field)
    {
        var kind = settings.Shape.Kind;
        return field switch
        {
            "emitFrom" => kind is ParticleShapeKind.Rectangle or ParticleShapeKind.Circle or ParticleShapeKind.Tile or ParticleShapeKind.Polygon,
            "radius" => kind is ParticleShapeKind.Circle or ParticleShapeKind.Cone,
            "innerRadius" or "arc" => kind == ParticleShapeKind.Circle,
            "size" => kind is ParticleShapeKind.Rectangle or ParticleShapeKind.Line,
            "coneAngle" => kind == ParticleShapeKind.Cone,
            "tile" or "cellSize" => kind == ParticleShapeKind.Tile,
            "points" => kind == ParticleShapeKind.Polygon,
            "angle" => settings.Shape.Direction == ParticleDirectionMode.Fixed || kind == ParticleShapeKind.Cone,
            _ => true
        };
    }

    private static string CurveSummary(Curve curve)
    {
        var keys = curve.Keys;
        if (keys.Length == 0)
            return "×1";
        return keys.Length == 1 ? $"×{Number(keys[0].Value)}" : $"×{Number(keys[0].Value)} → ×{Number(keys[^1].Value)}";
    }

    private static string? Join(params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return present.Length == 0 ? null : string.Join(" · ", present);
    }

    private static string Range(MinMaxFloat range) => range.IsConstant() ? Number(range.Min) : $"{Number(range.Min)}–{Number(range.Max)}";

    private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Degrees(float radians) => $"{(radians * 180 / MathF.PI).ToString("0.#", CultureInfo.InvariantCulture)}°";

    private static string Seconds(float seconds) => $"{Number(seconds)} s";

    private static string Humanize(string name) => Fields.SavedValues.Humanize(char.ToLowerInvariant(name[0]) + name[1..]);
}
