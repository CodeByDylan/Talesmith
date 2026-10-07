using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Rendering.Tests;

/// <summary>Builds lighting test frames: a checkered floor, pillars that cast shadows and an unlit overlay bar.</summary>
public static class LightingScenes
{
    public const int Width = 640;
    public const int Height = 360;

    public static readonly Rect2 OverlayBar = new(16, 16, 160, 20);

    public static readonly Rect2[] Pillars = [new(250, 150, 40, 40), new(430, 230, 30, 60)];

    public static readonly SpriteInstance Crate = SpriteInstance.Create(new Vector2(150, 290), new Vector2(36, 36), new Rect2(0, 0, 1, 1), new Color(150, 96, 80), new Vector2(0.5f), 0.5f);

    public static RenderFrame Begin(IRenderer renderer, Camera2D? camera = null)
    {
        var frame = new RenderFrame();
        frame.Begin(camera ?? new Camera2D(new Vector2(Width / 2f, Height / 2f)), new Vector2(Width, Height), new Color(20, 22, 30), 0, renderer.WhiteTexture);
        return frame;
    }

    public static void DrawContent(RenderFrame frame)
    {
        for (var y = 0; y < Height; y += 32)
        {
            for (var x = 0; x < Width; x += 32)
            {
                var even = (x / 32 + y / 32) % 2 == 0;
                frame.FillRect(new Rect2(x, y, 32, 32), even ? new Color(168, 160, 140) : new Color(148, 140, 122), RenderLayers.Terrain);
            }
        }

        foreach (var pillar in Pillars)
            frame.FillRect(pillar, new Color(96, 104, 130), RenderLayers.Entities);
        frame.Draw(frame.WhiteTexture, null, Crate, RenderLayers.Entities);
        frame.FillRect(OverlayBar, Color.White, RenderLayers.Overlay);
    }

    public static void AddOccluders(LightingFrame lighting)
    {
        foreach (var pillar in Pillars)
            lighting.AddOccluder([pillar.Position, new(pillar.Right, pillar.Top), new(pillar.Right, pillar.Bottom), new(pillar.Left, pillar.Bottom)], 1, false);
        var m = Crate.Transform;
        lighting.AddOccluder([Vector2.Transform(Vector2.Zero, m), Vector2.Transform(Vector2.UnitX, m), Vector2.Transform(Vector2.One, m), Vector2.Transform(Vector2.UnitY, m)], 1, false);
    }

    public static FrameLight Point(Vector2 position, float radius, Vector3 color, bool shadows = true, float softness = 0.5f) => new()
    {
        Type = LightType.Point,
        Blend = LightBlend.Additive,
        Position = position,
        Direction = Vector2.UnitX,
        Color = color,
        Radius = radius,
        Falloff = 1.6f,
        CastsShadows = shadows,
        ShadowStrength = 1,
        ShadowSoftness = softness,
        ShadowLayers = uint.MaxValue
    };

    public static FrameLight Spot(Vector2 position, Vector2 target, float radius, Vector3 color, float angleDegrees) => new()
    {
        Type = LightType.Spot,
        Blend = LightBlend.Additive,
        Position = position,
        Direction = Vector2.Normalize(target - position),
        Color = color,
        Radius = radius,
        Falloff = 1.4f,
        SpotAngle = angleDegrees * MathF.PI / 180,
        SpotInnerAngle = angleDegrees * 0.5f * MathF.PI / 180,
        CastsShadows = true,
        ShadowStrength = 1,
        ShadowSoftness = 0.4f,
        ShadowLayers = uint.MaxValue
    };

    /// <summary>The showcase: a dim blue night, a warm point light and a cool spot light, both with soft shadows.</summary>
    public static RenderFrame Night(IRenderer renderer, int samples = 7, float resolution = 0.5f, Camera2D? camera = null)
    {
        var frame = Begin(renderer, camera);
        DrawContent(frame);
        var lighting = frame.Lighting;
        lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.10f, 0.11f, 0.20f), ShadowSamples = samples, ResolutionScale = resolution, ShadowResolution = 1024 });
        lighting.AddLight(Point(new Vector2(170, 170), 330, new Vector3(1.0f, 0.72f, 0.42f) * 1.6f));
        lighting.AddLight(Spot(new Vector2(600, 30), new Vector2(380, 280), 420, new Vector3(0.45f, 0.75f, 1.0f) * 1.5f, 50));
        AddOccluders(lighting);
        frame.Finish();
        return frame;
    }
}
