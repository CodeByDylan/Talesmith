using System.Numerics;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;
using static Talesmith.Rendering.Tests.OffscreenRenderers;

namespace Talesmith.Rendering.Tests;

/// <summary>Renders lit frames offscreen with every available backend and checks the light map where it matters.</summary>
public sealed class LightingRenderTests
{
    public static TheoryData<string> AvailableBackends => Backends;

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void NightSceneIsLitAroundLightsAndDarkElsewhere(string backend)
    {
        using var renderer = Create(backend);
        var image = Render(renderer, LightingScenes.Night(renderer));
        Save(image, $"night-{backend}");
        Save(Render(renderer, LightingScenes.Night(renderer, camera: new Camera2D(new Vector2(340, 150), 3))), $"night-closeup-{backend}");

        var nearLight = Brightness(Pixel(image, 200, 150));
        var farCorner = Brightness(Pixel(image, 620, 340));
        Assert.True(nearLight > 0.6f, $"Near the light: {nearLight}");
        Assert.True(farCorner < 0.15f, $"Unlit corner: {farCorner}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void PillarsCastShadowsAwayFromTheLight(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.05f) });
        frame.Lighting.AddLight(LightingScenes.Point(new Vector2(150, 170), 400, new Vector3(1.5f), softness: 0.2f));
        LightingScenes.AddOccluders(frame.Lighting);
        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"shadow-{backend}");

        // Behind the first pillar, seen from the light, versus the same distance away above it.
        var shadowed = Brightness(Pixel(image, 340, 175));
        var open = Brightness(Pixel(image, 330, 95));
        Assert.True(shadowed < open * 0.35f, $"Shadowed {shadowed}, open {open}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void SelfShadowsOffKeepTheCasterLit(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.05f) });
        frame.Lighting.AddLight(LightingScenes.Point(new Vector2(150, 170), 400, new Vector3(1.5f), softness: 0));
        LightingScenes.AddOccluders(frame.Lighting);
        frame.Finish();
        var image = Render(renderer, frame);

        var pillarFace = Brightness(Pixel(image, 270, 170));
        Assert.True(pillarFace > 0.3f, $"Pillar face: {pillarFace}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void CastersInAnotherCastersShadowStayLitInside(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.05f) });
        frame.Lighting.AddLight(LightingScenes.Point(new Vector2(150, 170), 400, new Vector3(1.5f), softness: 0.2f));
        LightingScenes.AddOccluders(frame.Lighting);
        // A crate behind the first pillar, and a second one stacked on it, as crates next to each other are.
        frame.Lighting.AddOccluder([new(340, 155), new(370, 155), new(370, 185), new(340, 185)], 1, false);
        frame.Lighting.AddOccluder([new(340, 125), new(370, 125), new(370, 155), new(340, 155)], 1, false);
        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"shadowed-casters-{backend}");

        var between = Brightness(Pixel(image, 315, 170));
        var crate = Brightness(Pixel(image, 360, 170));
        var stacked = Brightness(Pixel(image, 360, 140));
        Assert.True(crate > between * 3, $"Crate {crate}, shadow in front of it {between}");
        Assert.True(MathF.Abs(stacked - crate) < 0.15f, $"Stacked crate {stacked}, crate {crate}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void EveryCasterAlongALightStaysLit(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.05f) });
        var visible = frame.VisibleBounds.Inflate(32);
        frame.Lighting.AddLight(new FrameLight
        {
            Type = LightType.Directional,
            Blend = LightBlend.Additive,
            Position = visible.Center,
            Radius = visible.Size.Length() / 2,
            Direction = Vector2.UnitX,
            Color = new Vector3(1.2f),
            CastsShadows = true,
            ShadowStrength = 1,
            ShadowLayers = uint.MaxValue
        });
        // Four crates in a row along the light, with gaps between them that lie in the shadow.
        for (var i = 0; i < 4; i++)
        {
            var left = 100 + i * 70f;
            frame.Lighting.AddOccluder([new(left, 60), new(left + 30, 60), new(left + 30, 90), new(left, 90)], 1, false);
        }

        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"casters-in-a-row-{backend}");

        var first = Brightness(Pixel(image, 115, 75));
        for (var i = 1; i < 4; i++)
        {
            var crate = Brightness(Pixel(image, 115 + i * 70, 75));
            var gap = Brightness(Pixel(image, 150 + (i - 1) * 70, 75));
            Assert.True(crate > gap * 3, $"Crate {i} {crate}, gap in front of it {gap}");
            Assert.True(MathF.Abs(crate - first) < 0.15f, $"Crate {i} {crate}, first crate {first}");
        }
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void OverlayLayersAreNotLit(string backend)
    {
        using var renderer = Create(backend);
        var image = Render(renderer, LightingScenes.Night(renderer));

        var bar = LightingScenes.OverlayBar;
        Assert.Equal(Color.White, Pixel(image, (int)bar.Center.X, (int)bar.Center.Y));
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void FramesWithoutActiveLightingRenderExactlyAsBefore(string backend)
    {
        using var renderer = Create(backend);
        var plain = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(plain);
        plain.Finish();
        var expected = Render(renderer, plain);

        var neutral = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(neutral);
        neutral.Lighting.Enable(LightMapSettings.Default);
        LightingScenes.AddOccluders(neutral.Lighting);
        neutral.Finish();
        Assert.False(neutral.Lighting.IsActive);

        Assert.Equal(expected.Pixels, Render(renderer, neutral).Pixels);
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void EmissiveSpritesIgnoreDarkness(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        var glow = new Rect2(500, 60, 40, 40);
        var instance = new SpriteInstance(Matrix3x2.CreateScale(glow.Size) * Matrix3x2.CreateTranslation(glow.Position), new Rect2(0, 0, 1, 1), Color.White);
        frame.Draw(renderer.WhiteTexture, null, instance with { Tint = new Color(255, 200, 80) }, RenderLayers.Entities);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.05f) });
        frame.Lighting.AddEmissive(renderer.WhiteTexture, instance, 1);
        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"emissive-{backend}");

        var lamp = Pixel(image, 520, 80);
        Assert.InRange(lamp.R, 245, 255);
        Assert.InRange(lamp.G, 195, 215);
        Assert.True(Brightness(Pixel(image, 560, 80)) < 0.1f);
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void MultiplyLightsDarkenAndSpotLightsStayInTheirCone(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default);
        frame.Lighting.AddLight(LightingScenes.Spot(new Vector2(320, 20), new Vector2(320, 300), 340, new Vector3(1), 40) with { CastsShadows = false });
        frame.Lighting.AddLight(LightingScenes.Point(new Vector2(560, 280), 120, new Vector3(0.2f), shadows: false) with { Blend = LightBlend.Multiply, Falloff = 1 });
        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"spot-multiply-{backend}");

        var inCone = Brightness(Pixel(image, 320, 120));
        var besideCone = Brightness(Pixel(image, 200, 120));
        var darkened = Brightness(Pixel(image, 560, 280));
        Assert.True(inCone > besideCone * 1.4f, $"In cone {inCone}, beside {besideCone}");
        Assert.True(darkened < besideCone * 0.5f, $"Darkened {darkened}, ambient {besideCone}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void DirectionalLightsCastParallelShadowsOfLimitedLength(string backend)
    {
        using var renderer = Create(backend);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.25f, 0.22f, 0.35f) });
        var visible = frame.VisibleBounds.Inflate(32);
        frame.Lighting.AddLight(new FrameLight
        {
            Type = LightType.Directional,
            Blend = LightBlend.Additive,
            Position = visible.Center,
            Radius = visible.Size.Length() / 2,
            Direction = Vector2.Normalize(new Vector2(1, 0.6f)),
            Color = new Vector3(1.0f, 0.85f, 0.6f),
            CastsShadows = true,
            ShadowStrength = 0.85f,
            ShadowSoftness = 0.3f,
            ShadowLength = 90,
            ShadowLayers = uint.MaxValue
        });
        LightingScenes.AddOccluders(frame.Lighting);
        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"directional-{backend}");

        var shadowed = Brightness(Pixel(image, 310, 205));
        var beyondShadow = Brightness(Pixel(image, 420, 270));
        var open = Brightness(Pixel(image, 330, 110));
        Assert.True(shadowed < open * 0.7f, $"Shadowed {shadowed}, open {open}");
        Assert.True(beyondShadow > open * 0.85f, $"Beyond the shadow's length {beyondShadow}, open {open}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void CookiesShapeTheLight(string backend)
    {
        using var renderer = Create(backend);
        var cookie = renderer.CreateTexture(WindowCookie(64));
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.1f) });
        frame.Lighting.AddLight(LightingScenes.Point(new Vector2(320, 180), 170, new Vector3(1.6f), shadows: false) with { Cookie = cookie, Falloff = 0.6f });
        frame.Finish();
        var image = Render(renderer, frame);
        Save(image, $"cookie-{backend}");

        // The cookie's cross-shaped frame blocks the light along the axes through the light.
        var pane = Brightness(Pixel(image, 360, 220));
        var bar = Brightness(Pixel(image, 360, 180));
        Assert.True(bar < pane * 0.5f, $"Window bar {bar}, pane {pane}");
    }

    private static ImageData WindowCookie(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var frame = Math.Abs(x - size / 2) < 4 || Math.Abs(y - size / 2) < 4 || x < 4 || y < 4 || x >= size - 4 || y >= size - 4;
                var value = (byte)(frame ? 0 : 255);
                var i = (y * size + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }

        return new ImageData(size, size, pixels);
    }

    private static ImageData Render(IRenderer renderer, RenderFrame frame) =>
        ((IOffscreenRenderer)renderer).RenderToImage(frame, LightingScenes.Width, LightingScenes.Height);
}
