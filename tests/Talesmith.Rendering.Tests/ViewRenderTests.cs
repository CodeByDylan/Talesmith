using System.Numerics;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering.Lighting;
using static Talesmith.Rendering.Tests.OffscreenRenderers;

namespace Talesmith.Rendering.Tests;

/// <summary>Renders frames laid out by <see cref="ViewLayout"/> with every available backend: bars, framing, screen space, lighting and post effects.</summary>
public sealed class ViewRenderTests
{
    private static readonly ViewSettings Design = new() { Width = 200, Height = 100, BorderColor = new Color(255, 0, 255) };
    private static readonly Color Background = new(0, 128, 0);
    private static readonly Color Block = new(255, 0, 0);
    private static readonly Color Hud = new(0, 0, 255);
    private static readonly Rect2 BlockBounds = new(20, 10, 60, 30);
    private static readonly Rect2 HudBounds = new(150, 70, 30, 20);

    private static readonly ShaderSource ViewGradient = new("view-gradient",
        "uniform shader scene; uniform float2 resolution; uniform float time; uniform float4 params[4];"
        + "half4 main(float2 coord) { return half4(half2(coord / resolution), scene.eval(coord).b, 1); }",
        LoadSpirV("view-gradient.frag.spv"));

    public static TheoryData<string> AvailableBackends => Backends;

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void FitFillsTheBarsWithTheBorderColor(string backend)
    {
        using var renderer = Create(backend);
        var image = Render(renderer, 400, 300, out var layout);
        Save(image, $"view-letterbox-{backend}");

        Assert.Equal(new Rect2(0, 50, 400, 200), layout.ViewRect);
        Assert.Equal(Design.BorderColor, Pixel(image, 200, 0));
        Assert.Equal(Design.BorderColor, Pixel(image, 200, 49));
        Assert.Equal(Background, Pixel(image, 200, 50));
        Assert.Equal(Background, Pixel(image, 200, 249));
        Assert.Equal(Design.BorderColor, Pixel(image, 200, 250));
        Assert.Equal(Design.BorderColor, Pixel(image, 399, 299));
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void PillarboxesKeepTheContentCentered(string backend)
    {
        using var renderer = Create(backend);
        var image = Render(renderer, 501, 200, out var layout);
        Save(image, $"view-pillarbox-{backend}");

        Assert.Equal(new Rect2(50, 0, 400, 200), layout.ViewRect);
        Assert.Equal(Design.BorderColor, Pixel(image, 49, 100));
        Assert.Equal(Background, Pixel(image, 50, 100));
        Assert.Equal(Background, Pixel(image, 449, 100));
        Assert.Equal(Design.BorderColor, Pixel(image, 450, 100));
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void WorldAndScreenContentKeepTheirPlaceInTheViewAtEverySize(string backend)
    {
        using var renderer = Create(backend);
        var small = Render(renderer, 400, 300, out var smallLayout);
        var large = Render(renderer, 1000, 450, out var largeLayout);
        Save(large, $"view-large-{backend}");

        for (var y = 2.5f; y < 100; y += 5)
        {
            for (var x = 2.5f; x < 200; x += 5)
            {
                var view = new Vector2(x, y);
                var expected = HudBounds.Contains(view) ? Hud : BlockBounds.Contains(view) ? Block : Background;
                Assert.Equal(expected, At(small, smallLayout, view));
                Assert.Equal(expected, At(large, largeLayout, view));
            }
        }
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void ExpandDrawsOnTheWholeTarget(string backend)
    {
        using var renderer = Create(backend);
        var image = Render(renderer, 400, 300, out var layout, Design with { ScaleMode = ViewScaleMode.Expand });

        Assert.Equal(new Vector2(200, 150), layout.ViewSize);
        Assert.Equal(Background, Pixel(image, 200, 0));
        Assert.Equal(Background, Pixel(image, 399, 299));
        Assert.Equal(Hud, At(image, layout, HudBounds.Center));
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void LightingStaysInsideTheView(string backend)
    {
        using var renderer = Create(backend);
        var images = new List<(ImageData Image, ViewLayout Layout)>();
        foreach (var (width, height) in new[] { (400, 300), (1000, 450) })
        {
            var layout = ViewLayout.Compute(new Vector2(width, height), 1, Design);
            var frame = Begin(renderer, layout);
            frame.FillRect(new Rect2(0, 0, 200, 100), new Color(200, 200, 200), RenderLayers.Terrain);
            frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.05f) });
            frame.Lighting.AddLight(LightingScenes.Point(new Vector2(50, 50), 40, new Vector3(1.5f), shadows: false));
            frame.Finish();
            var image = ((IOffscreenRenderer)renderer).RenderToImage(frame, width, height);
            Save(image, $"view-lit-{width}x{height}-{backend}");
            images.Add((image, layout));
        }

        foreach (var (image, layout) in images)
        {
            Assert.Equal(Design.BorderColor, Pixel(image, 0, 0));
            Assert.Equal(Design.BorderColor, Pixel(image, image.Width - 1, image.Height - 1));
            var lit = Brightness(At(image, layout, new Vector2(50, 50)));
            var dark = Brightness(At(image, layout, new Vector2(170, 20)));
            Assert.True(lit > 0.5f, $"Under the light: {lit}");
            Assert.True(dark < 0.1f, $"Far from the light: {dark}");
        }
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void PostEffectsCoverOnlyTheView(string backend)
    {
        using var renderer = Create(backend);
        var layout = ViewLayout.Compute(new Vector2(400, 300), 1, Design);
        var frame = Begin(renderer, layout);
        frame.AddPostEffect(new PostEffect(ViewGradient));
        frame.Finish();
        var image = ((IOffscreenRenderer)renderer).RenderToImage(frame, 400, 300);
        Save(image, $"view-post-effect-{backend}");

        Assert.Equal(Design.BorderColor, Pixel(image, 200, 10));
        Assert.Equal(Design.BorderColor, Pixel(image, 200, 290));
        var topLeft = Pixel(image, 0, 50);
        var bottomRight = Pixel(image, 399, 249);
        Assert.True(topLeft.R < 4 && topLeft.G < 4, $"Top-left of the view: {topLeft}");
        Assert.True(bottomRight.R > 250 && bottomRight.G > 250, $"Bottom-right of the view: {bottomRight}");
    }

    [Theory]
    [MemberData(nameof(AvailableBackends))]
    public void BarsFollowLayoutChangesAtTheSameTargetSize(string backend)
    {
        using var renderer = Create(backend);
        for (var i = 0; i < 4; i++)
        {
            var settings = i % 2 == 0 ? Design : Design with { Width = 100, BorderColor = new Color(255, 255, 0) };
            var image = Render(renderer, 400, 300, out var layout, settings);
            Assert.Equal(settings.BorderColor, Pixel(image, (int)layout.ViewRect.X / 2, (int)layout.ViewRect.Y / 2));
            Assert.Equal(Background, At(image, layout, new Vector2(5, 5)));
        }
    }

    private static ImageData Render(IRenderer renderer, int width, int height, out ViewLayout layout, ViewSettings? settings = null)
    {
        layout = ViewLayout.Compute(new Vector2(width, height), 1, settings ?? Design);
        var frame = Begin(renderer, layout, (settings ?? Design).BorderColor);
        frame.FillRect(BlockBounds, Block, RenderLayers.Entities);
        frame.FillRect(HudBounds, Hud, RenderLayers.Overlay, RenderSpace.Screen);
        frame.Finish();
        return ((IOffscreenRenderer)renderer).RenderToImage(frame, width, height);
    }

    private static RenderFrame Begin(IRenderer renderer, in ViewLayout layout, Color? border = null)
    {
        var frame = new RenderFrame();
        frame.Begin(new Camera2D(layout.ViewSize / 2), layout, Background, border ?? Design.BorderColor, 0, renderer.WhiteTexture);
        return frame;
    }

    private static Color At(ImageData image, in ViewLayout layout, Vector2 view)
    {
        var target = layout.ViewToTarget(view);
        return Pixel(image, (int)target.X, (int)target.Y);
    }

    private static byte[] LoadSpirV(string name)
    {
        using var stream = typeof(ViewRenderTests).Assembly.GetManifestResourceStream("Shaders." + name)!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
