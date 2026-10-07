using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.VFX.Presets;

/// <summary>A preset that ships with the engine.</summary>
/// <param name="Create">Creates a fresh copy of the settings each time, so edits never change the original.</param>
public sealed record BuiltInParticlePreset(string Name, string Description, Func<ParticleSettings> Create)
{
    public ParticlePreset CreatePreset() => new(Name, Create());
}

/// <summary>Ready-made effects tuned for a 2D scene at one world unit per pixel, using only built-in textures.</summary>
public static class BuiltInParticlePresets
{
    public static IReadOnlyList<BuiltInParticlePreset> All { get; } =
    [
        new("Fire", "A flickering flame with licking tongues of light.", Fire),
        new("Smoke", "Soft, billowing smoke drifting up and away.", Smoke),
        new("Sparks", "A fountain of hot sparks that arc and fade.", Sparks),
        new("Magic sparkle", "Twinkling stars swirling in a soft glow.", MagicSparkle),
        new("Rain", "Slanted streaks of rain across the view.", Rain),
        new("Snow", "Flakes drifting down and swaying in the wind.", Snow),
        new("Dust puff", "A puff of dust, for landings and footsteps.", DustPuff),
        new("Explosion", "A fireball that cools into a smoke cloud.", Explosion)
    ];

    public static BuiltInParticlePreset? Find(string name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ParticleSettings Fire()
    {
        var settings = new ParticleSettings { Duration = 1, Prewarm = true, MaxParticles = 400 };
        settings.Emission.RateOverTime = 90;
        settings.Shape.Kind = ParticleShapeKind.Circle;
        settings.Shape.Radius = 20;
        settings.Shape.Spread = Degrees(10);
        settings.Initial.Lifetime = new(0.6f, 1);
        settings.Initial.Speed = new(80, 140);
        settings.Initial.Size = new(36, 56);
        settings.Initial.Rotation = new(0, MathF.Tau);
        settings.Initial.AngularVelocity = new(Degrees(-100), Degrees(100));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0;
        settings.Forces.Acceleration = new Vector2(0, -240);
        settings.Drag.Enabled = true;
        settings.Drag.Drag = 0.5f;
        settings.Noise.Enabled = true;
        settings.Noise.Strength = 260;
        settings.Noise.Scale = 60;
        settings.Noise.ScrollSpeed = 1.6f;
        settings.Noise.StrengthOverLifetime = Curve.Linear(0.3f, 1);
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient(
        [
            new(0, Hex("#00FFE8A8")),
            new(0.06f, Hex("#5CFFC860")),
            new(0.25f, Hex("#5CFF8C2A")),
            new(0.5f, Hex("#4DE5521A")),
            new(0.75f, Hex("#26A0280A")),
            new(1, Hex("#00500C00"))
        ]);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = new Curve([new(0, 0.8f, 0, 2), new(0.12f, 1), new(1, 0.2f, -0.9f, -0.9f)]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Smoke;
        settings.Renderer.Blend = BlendMode.Additive;
        settings.Renderer.Alignment = ParticleAlignment.Stretch;
        settings.Renderer.StretchLengthScale = 0.85f;
        settings.Renderer.StretchSpeedScale = 0.004f;
        return settings;
    }

    public static ParticleSettings Smoke()
    {
        var settings = new ParticleSettings { Duration = 2, Prewarm = true, MaxParticles = 300 };
        settings.Emission.RateOverTime = 11;
        settings.Shape.Kind = ParticleShapeKind.Circle;
        settings.Shape.Radius = 8;
        settings.Shape.Spread = Degrees(22);
        settings.Initial.Lifetime = new(4, 5.5f);
        settings.Initial.Speed = new(50, 75);
        settings.Initial.Size = new(26, 40);
        settings.Initial.Rotation = new(0, MathF.Tau);
        settings.Initial.AngularVelocity = new(Degrees(-25), Degrees(25));
        settings.Initial.Color = new MinMaxColor(Hex("#5E5C63"), Hex("#8A8890"));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0;
        settings.Forces.Acceleration = new Vector2(16, -12);
        settings.Drag.Enabled = true;
        settings.Drag.Drag = 0.25f;
        settings.Noise.Enabled = true;
        settings.Noise.Strength = 30;
        settings.Noise.Scale = 160;
        settings.Noise.ScrollSpeed = 0.3f;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient(
        [
            new(0, Hex("#00FFFFFF")),
            new(0.1f, Hex("#73F2F2F2")),
            new(0.5f, Hex("#4AFFFFFF")),
            new(1, Hex("#00FFFFFF"))
        ]);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = new Curve([new(0, 0.6f, 0, 4), new(0.3f, 1.6f, 2.6f, 2.6f), new(1, 3.2f, 1.6f, 0)]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Smoke;
        settings.Renderer.SortMode = ParticleSortMode.OldestInFront;
        return settings;
    }

    public static ParticleSettings Sparks()
    {
        var settings = new ParticleSettings { Duration = 1.5f, MaxParticles = 400 };
        settings.Emission.RateOverTime = 60;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(25, 35)));
        settings.Shape.Kind = ParticleShapeKind.Point;
        settings.Shape.Spread = Degrees(120);
        settings.Initial.Lifetime = new(0.6f, 1.3f);
        settings.Initial.Speed = new(220, 460);
        settings.Initial.Size = new(6, 9);
        settings.Initial.Color = new MinMaxColor(Hex("#FFF4C8"), Hex("#FFC060"));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0.9f;
        settings.Drag.Enabled = true;
        settings.Drag.Drag = 0.6f;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient(
        [
            new(0, Hex("#FFFFFFFF")),
            new(0.3f, Hex("#FFFFD27A")),
            new(0.7f, Hex("#E6FF7A26")),
            new(1, Hex("#00C8300A"))
        ]);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = Curve.Linear(1, 0.35f);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Streak;
        settings.Renderer.Blend = BlendMode.Additive;
        settings.Renderer.Alignment = ParticleAlignment.Stretch;
        settings.Renderer.StretchSpeedScale = 0.025f;
        settings.Renderer.StretchLengthScale = 1.2f;
        return settings;
    }

    public static ParticleSettings MagicSparkle()
    {
        var settings = new ParticleSettings { Duration = 2, Prewarm = true, MaxParticles = 300 };
        settings.Emission.RateOverTime = 60;
        settings.Shape.Kind = ParticleShapeKind.Circle;
        settings.Shape.Radius = 56;
        settings.Shape.Direction = ParticleDirectionMode.Random;
        settings.Initial.Lifetime = new(1, 2);
        settings.Initial.Speed = new(8, 30);
        settings.Initial.Size = new(18, 44);
        settings.Initial.Rotation = new(0, MathF.Tau);
        settings.Initial.AngularVelocity = new(Degrees(-140), Degrees(140));
        settings.Initial.Color = new MinMaxColor(Hex("#9AE6FF"), Hex("#E2A6FF"));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0;
        settings.Forces.Acceleration = new Vector2(0, -28);
        settings.VelocityOverLifetime.Enabled = true;
        settings.VelocityOverLifetime.Orbital = 0.9f;
        settings.Noise.Enabled = true;
        settings.Noise.Strength = 36;
        settings.Noise.Scale = 90;
        settings.Noise.ScrollSpeed = 0.6f;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient(
        [
            new(0, Hex("#00FFFFFF")),
            new(0.15f, Hex("#FFFFFFFF")),
            new(0.6f, Hex("#D9FFFFFF")),
            new(1, Hex("#00FFFFFF"))
        ]);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = new Curve([new(0, 0), new(0.18f, 1), new(0.6f, 0.75f), new(1, 0)]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Sparkle;
        settings.Renderer.Blend = BlendMode.Additive;
        return settings;
    }

    public static ParticleSettings Rain()
    {
        var settings = new ParticleSettings { Duration = 1, Prewarm = true, MaxParticles = 2500 };
        settings.Emission.RateOverTime = 420;
        settings.Shape.Kind = ParticleShapeKind.Line;
        settings.Shape.Size = new Vector2(1000, 0);
        settings.Shape.Angle = Degrees(102);
        settings.Shape.Spread = Degrees(2);
        settings.Initial.Lifetime = new(0.8f, 1.05f);
        settings.Initial.Speed = new(850, 1050);
        settings.Initial.Size = new(4, 5.5f);
        settings.Initial.Color = new MinMaxColor(Hex("#80A8C4F0"), Hex("#C0D0E4FF"));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0.3f;
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Streak;
        settings.Renderer.Alignment = ParticleAlignment.Stretch;
        settings.Renderer.StretchSpeedScale = 0.016f;
        settings.Renderer.StretchLengthScale = 1;
        return settings;
    }

    public static ParticleSettings Snow()
    {
        var settings = new ParticleSettings { Duration = 4, Prewarm = true, MaxParticles = 1500 };
        settings.Emission.RateOverTime = 70;
        settings.Shape.Kind = ParticleShapeKind.Line;
        settings.Shape.Size = new Vector2(1000, 0);
        settings.Shape.Angle = Degrees(90);
        settings.Shape.Spread = Degrees(30);
        settings.Initial.Lifetime = new(6, 9);
        settings.Initial.Speed = new(35, 65);
        settings.Initial.Size = new(4, 10);
        settings.Initial.Color = new MinMaxColor(Hex("#B4FFFFFF"), Hex("#FFFFFFFF"));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0.02f;
        settings.Forces.Acceleration = new Vector2(10, 0);
        settings.Drag.Enabled = true;
        settings.Drag.Drag = 0.3f;
        settings.Noise.Enabled = true;
        settings.Noise.Strength = 40;
        settings.Noise.Scale = 170;
        settings.Noise.ScrollSpeed = 0.25f;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient(
        [
            new(0, Hex("#00FFFFFF")),
            new(0.06f, Hex("#FFFFFFFF")),
            new(0.85f, Hex("#FFFFFFFF")),
            new(1, Hex("#00FFFFFF"))
        ]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.SoftCircle;
        return settings;
    }

    public static ParticleSettings DustPuff()
    {
        var settings = new ParticleSettings { Duration = 1, Looping = false, MaxParticles = 64 };
        settings.Emission.RateOverTime = 0;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(20, 28)));
        settings.Shape.Kind = ParticleShapeKind.Line;
        settings.Shape.Size = new Vector2(28, 0);
        settings.Shape.Direction = ParticleDirectionMode.Shape;
        settings.Shape.Spread = Degrees(170);
        settings.Initial.Lifetime = new(0.6f, 1.1f);
        settings.Initial.Speed = new(110, 240);
        settings.Initial.Size = new(26, 44);
        settings.Initial.Rotation = new(0, MathF.Tau);
        settings.Initial.AngularVelocity = new(Degrees(-70), Degrees(70));
        settings.Initial.Color = new MinMaxColor(Hex("#CFC2AA"), Hex("#A89A82"));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0;
        settings.Forces.Acceleration = new Vector2(0, -24);
        settings.Drag.Enabled = true;
        settings.Drag.Drag = 4.5f;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient([new(0, Hex("#A6FFFFFF")), new(0.35f, Hex("#73FFFFFF")), new(1, Hex("#00FFFFFF"))]);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = new Curve([new(0, 0.5f, 0, 2.5f), new(1, 1.5f, 0.3f, 0)]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Smoke;
        settings.Renderer.SortMode = ParticleSortMode.OldestInFront;
        return settings;
    }

    public static ParticleSettings Explosion()
    {
        var settings = new ParticleSettings { Duration = 2, Looping = false, MaxParticles = 200 };
        settings.Emission.RateOverTime = 0;
        settings.Emission.Bursts.Add(new ParticleBurst(0, new MinMaxFloat(60, 75)));
        settings.Emission.Bursts.Add(new ParticleBurst(0.04f, new MinMaxFloat(15, 22)));
        settings.Shape.Kind = ParticleShapeKind.Circle;
        settings.Shape.Radius = 12;
        settings.Shape.Direction = ParticleDirectionMode.Shape;
        settings.Initial.Lifetime = new(0.9f, 1.8f);
        settings.Initial.Speed = new(10, 320);
        settings.Initial.Size = new(40, 72);
        settings.Initial.Rotation = new(0, MathF.Tau);
        settings.Initial.AngularVelocity = new(Degrees(-80), Degrees(80));
        settings.Forces.Enabled = true;
        settings.Forces.GravityScale = 0;
        settings.Forces.Acceleration = new Vector2(0, -45);
        settings.Drag.Enabled = true;
        settings.Drag.Drag = 3.8f;
        settings.Noise.Enabled = true;
        settings.Noise.Strength = 40;
        settings.Noise.Scale = 120;
        settings.Noise.ScrollSpeed = 0.5f;
        settings.ColorOverLifetime.Enabled = true;
        settings.ColorOverLifetime.Color = new Gradient(
        [
            new(0, Hex("#FFFFFDE8")),
            new(0.1f, Hex("#FFFFD957")),
            new(0.25f, Hex("#FFFF7A1F")),
            new(0.42f, Hex("#F2A8361A")),
            new(0.58f, Hex("#E0603A26")),
            new(0.78f, Hex("#A6403C3C")),
            new(1, Hex("#00353333"))
        ]);
        settings.SizeOverLifetime.Enabled = true;
        settings.SizeOverLifetime.Size = new Curve([new(0, 0.35f, 0, 5), new(0.2f, 1, 1.2f, 1.2f), new(1, 1.7f, 0.5f, 0)]);
        settings.Renderer.BuiltInTexture = BuiltInParticleTexture.Smoke;
        settings.Renderer.SortMode = ParticleSortMode.YoungestInFront;
        return settings;
    }

    private static float Degrees(float degrees) => MathHelper.ToRadians(degrees);

    private static Color Hex(string text) => Color.Parse(text);
}
