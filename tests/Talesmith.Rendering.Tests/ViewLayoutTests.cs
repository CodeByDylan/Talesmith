using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Rendering.Tests;

public sealed class ViewLayoutTests
{
    private static readonly ViewSettings Design = new() { Width = 640, Height = 360 };

    public static TheoryData<ViewScaleMode, bool, int, int, float> EveryModeAndWindow
    {
        get
        {
            var data = new TheoryData<ViewScaleMode, bool, int, int, float>();
            (int, int)[] windows = [(1280, 720), (1920, 1080), (1366, 768), (1280, 800), (720, 1280), (3440, 1440), (2560, 1080), (641, 361), (300, 200), (7, 3), (1, 1)];
            foreach (var mode in Enum.GetValues<ViewScaleMode>())
            {
                foreach (var integer in new[] { false, true })
                {
                    foreach (var (width, height) in windows)
                    {
                        foreach (var dpi in new[] { 1f, 1.5f, 2f })
                            data.Add(mode, integer, width, height, dpi);
                    }
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryModeAndWindow))]
    public void ViewRectIsWholePixelsCenteredInsideTheTarget(ViewScaleMode mode, bool integer, int width, int height, float dpi)
    {
        var layout = ViewLayout.Compute(new Vector2(width, height), dpi, Design with { ScaleMode = mode, IntegerScale = integer });
        var rect = layout.ViewRect;

        Assert.Equal(new Vector2(width, height), layout.TargetSize);
        Assert.True(rect.Width >= 1 && rect.Height >= 1, $"View {rect}");
        Assert.True(rect.X >= 0 && rect.Y >= 0 && rect.Right <= width && rect.Bottom <= height, $"View {rect} in {width}×{height}");
        foreach (var edge in new[] { rect.X, rect.Y, rect.Width, rect.Height })
            Assert.Equal(MathF.Round(edge), edge);
        Assert.InRange(rect.X - (width - rect.Right), -1, 0);
        Assert.InRange(rect.Y - (height - rect.Bottom), -1, 0);
        Assert.True(layout.Scale > 0 && float.IsFinite(layout.Scale));
    }

    [Theory]
    [MemberData(nameof(EveryModeAndWindow))]
    public void ViewSizeFollowsTheMode(ViewScaleMode mode, bool integer, int width, int height, float dpi)
    {
        var layout = ViewLayout.Compute(new Vector2(width, height), dpi, Design with { ScaleMode = mode, IntegerScale = integer });

        switch (mode)
        {
            case ViewScaleMode.Fit:
                Assert.Equal(new Vector2(640, 360), layout.ViewSize);
                break;
            case ViewScaleMode.Expand:
                Assert.True(Covers(layout.ViewSize, new Vector2(640, 360)) || layout.Scale < 1, $"Expand shows {layout.ViewSize}");
                Assert.True(Near(layout.ViewSize.X, 640) || Near(layout.ViewSize.Y, 360) || integer, $"Expand shows {layout.ViewSize}");
                break;
            case ViewScaleMode.Crop:
                Assert.True(Covers(new Vector2(640, 360), layout.ViewSize), $"Crop shows {layout.ViewSize}");
                Assert.True(Near(layout.ViewSize.X, 640) || Near(layout.ViewSize.Y, 360) || integer, $"Crop shows {layout.ViewSize}");
                break;
            default:
                Assert.Equal(new Vector2(width, height) / layout.Scale, layout.ViewSize);
                break;
        }

        if (mode != ViewScaleMode.Fit)
            Assert.Equal(new Rect2(0, 0, width, height), layout.ViewRect);
    }

    [Theory]
    [MemberData(nameof(EveryModeAndWindow))]
    public void IntegerScaleIsWholeFromOneUp(ViewScaleMode mode, bool integer, int width, int height, float dpi)
    {
        var settings = Design with { ScaleMode = mode, IntegerScale = integer };
        var layout = ViewLayout.Compute(new Vector2(width, height), dpi, settings);
        var raw = ViewLayout.Compute(new Vector2(width, height), dpi, settings with { IntegerScale = false }).Scale;

        if (!integer || raw < 1)
        {
            Assert.Equal(raw, layout.Scale);
            return;
        }

        Assert.Equal(MathF.Round(layout.Scale), layout.Scale);
        if (mode == ViewScaleMode.Crop)
            Assert.Equal(MathF.Ceiling(raw), layout.Scale);
        else
            Assert.Equal(MathF.Floor(raw), layout.Scale);
    }

    [Theory]
    [InlineData(1280, 720, 2)]
    [InlineData(1920, 1080, 3)]
    [InlineData(2560, 1440, 4)]
    [InlineData(3840, 2160, 6)]
    public void PixelArtDesignScalesWholeAtCommonResolutions(int width, int height, float scale)
    {
        var layout = ViewLayout.Compute(new Vector2(width, height), 1, Design with { IntegerScale = true });

        Assert.Equal(scale, layout.Scale);
        Assert.Equal(new Rect2(0, 0, width, height), layout.ViewRect);
        Assert.False(layout.HasBorders);
    }

    [Fact]
    public void FitLetterboxesAWindowTallerThanTheDesign()
    {
        var layout = ViewLayout.Compute(new Vector2(1280, 800), 1, Design);

        Assert.Equal(2, layout.Scale);
        Assert.Equal(new Rect2(0, 40, 1280, 720), layout.ViewRect);
        Assert.True(layout.HasBorders);
    }

    [Fact]
    public void FitPillarboxesAWindowWiderThanTheDesign()
    {
        var layout = ViewLayout.Compute(new Vector2(3440, 1440), 1, Design);

        Assert.Equal(4, layout.Scale);
        Assert.Equal(new Rect2(440, 0, 2560, 1440), layout.ViewRect);
    }

    [Fact]
    public void FitWithIntegerScaleLeavesTheRoundingAsBars()
    {
        var layout = ViewLayout.Compute(new Vector2(1366, 768), 1, Design with { IntegerScale = true });

        Assert.Equal(2, layout.Scale);
        Assert.Equal(new Rect2(43, 24, 1280, 720), layout.ViewRect);
    }

    [Fact]
    public void FitSnapsFractionalSizesToWholePixels()
    {
        var layout = ViewLayout.Compute(new Vector2(1366, 768), 1, new ViewSettings { Width = 1280, Height = 720 });

        Assert.Equal(768 / 720f, layout.Scale);
        Assert.Equal(new Rect2(0, 0, 1365, 768), layout.ViewRect);
    }

    [Fact]
    public void ExpandShowsMoreAlongTheLongerSide()
    {
        var wide = ViewLayout.Compute(new Vector2(2560, 1080), 1, Design with { ScaleMode = ViewScaleMode.Expand });
        var tall = ViewLayout.Compute(new Vector2(1080, 1920), 1, Design with { ScaleMode = ViewScaleMode.Expand });

        Assert.Equal(3, wide.Scale);
        Assert.Equal(new Vector2(2560 / 3f, 360), wide.ViewSize);
        Assert.Equal(1080 / 640f, tall.Scale);
        Assert.Equal(640, tall.ViewSize.X, 3);
        Assert.Equal(1920 / tall.Scale, tall.ViewSize.Y, 3);
    }

    [Fact]
    public void CropCutsAlongTheLongerSide()
    {
        var layout = ViewLayout.Compute(new Vector2(2560, 1080), 1, Design with { ScaleMode = ViewScaleMode.Crop });

        Assert.Equal(4, layout.Scale);
        Assert.Equal(new Vector2(640, 270), layout.ViewSize);
    }

    [Fact]
    public void CropWithIntegerScaleRoundsUpSoTheViewStillCovers()
    {
        var layout = ViewLayout.Compute(new Vector2(1366, 768), 1, Design with { ScaleMode = ViewScaleMode.Crop, IntegerScale = true });

        Assert.Equal(3, layout.Scale);
        Assert.Equal(new Vector2(1366 / 3f, 256), layout.ViewSize);
    }

    [Fact]
    public void BelowOneTheExactScaleIsUsed()
    {
        var layout = ViewLayout.Compute(new Vector2(320, 180), 1, Design with { IntegerScale = true });

        Assert.Equal(0.5f, layout.Scale);
        Assert.Equal(new Rect2(0, 0, 320, 180), layout.ViewRect);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(2, 2)]
    public void NoneUsesTheDisplayScaleOnTheWholeTarget(float dpi, float scale)
    {
        var layout = ViewLayout.Compute(new Vector2(1920, 1080), dpi, ViewSettings.Unscaled);

        Assert.Equal(scale, layout.Scale);
        Assert.Equal(new Vector2(1920, 1080) / scale, layout.ViewSize);
        Assert.False(layout.HasBorders);
    }

    [Fact]
    public void NoneWithIntegerScaleRoundsTheDisplayScaleDown()
    {
        var layout = ViewLayout.Compute(new Vector2(1920, 1080), 1.5f, ViewSettings.Unscaled with { IntegerScale = true });

        Assert.Equal(1, layout.Scale);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 10)]
    [InlineData(0.4f, 0.4f)]
    public void EmptyTargetsBecomeOnePixel(float width, float height)
    {
        var layout = ViewLayout.Compute(new Vector2(width, height), 0, Design);

        Assert.Equal(Vector2.One, Vector2.Min(layout.TargetSize, Vector2.One));
        Assert.True(layout.ViewRect.Width >= 1 && layout.ViewRect.Height >= 1);
        Assert.True(float.IsFinite(layout.Scale) && layout.Scale > 0);
    }

    [Fact]
    public void TargetAndViewUnitsRoundTrip()
    {
        var layout = ViewLayout.Compute(new Vector2(1600, 1000), 1, Design);

        Assert.Equal(Vector2.Zero, layout.TargetToView(layout.ViewRect.Position));
        Assert.Equal(new Vector2(640, 360), layout.TargetToView(new Vector2(layout.ViewRect.Right, layout.ViewRect.Bottom)), Tolerance);
        var target = new Vector2(400, 300);
        Assert.Equal(target, layout.ViewToTarget(layout.TargetToView(target)), Tolerance);
    }

    [Fact]
    public void PositionsOnTheBarsMapBeyondTheView()
    {
        var layout = ViewLayout.Compute(new Vector2(1280, 800), 1, Design);

        var top = layout.TargetToView(new Vector2(640, 10));
        var bottom = layout.TargetToView(new Vector2(640, 795));

        Assert.True(top.Y < 0, $"Top bar {top}");
        Assert.True(bottom.Y > 360, $"Bottom bar {bottom}");
    }

    [Theory]
    [InlineData(640, 400, true)]
    [InlineData(0, 40, true)]
    [InlineData(1279, 759, true)]
    [InlineData(640, 39, false)]
    [InlineData(640, 760, false)]
    [InlineData(640, 10, false)]
    public void ContainsTellsTheViewFromTheBars(float x, float y, bool expected)
    {
        var layout = ViewLayout.Compute(new Vector2(1280, 800), 1, Design);

        Assert.Equal(expected, layout.Contains(new Vector2(x, y)));
    }

    [Fact]
    public void WorldConversionsGoThroughTheViewRect()
    {
        var layout = ViewLayout.Compute(new Vector2(1280, 800), 1, Design);
        var camera = layout.ScaleCamera(new Camera2D(new Vector2(1000, 500)));

        Assert.Equal(2, camera.Zoom);
        Assert.Equal(new Vector2(1000, 500), layout.TargetToWorld(camera, new Vector2(640, 400)), Tolerance);
        Assert.Equal(new Vector2(680, 320), layout.TargetToWorld(camera, new Vector2(0, 40)), Tolerance);
        Assert.Equal(new Vector2(680, 320), layout.ViewToWorld(camera, Vector2.Zero), Tolerance);
        var world = new Vector2(812, 431);
        Assert.Equal(world, layout.TargetToWorld(camera, layout.WorldToTarget(camera, world)), Tolerance);
        Assert.Equal(world, layout.ViewToWorld(camera, layout.WorldToView(camera, world)), Tolerance);
        AssertNear(new Rect2(680, 320, 640, 360), layout.VisibleWorld(camera));
    }

    [Fact]
    public void FramesKeepTheSameWorldAreaAtEveryWindowSize()
    {
        var small = new RenderFrame();
        var large = new RenderFrame();
        var camera = new Camera2D(new Vector2(320, 180));
        small.Begin(camera, ViewLayout.Compute(new Vector2(1280, 720), 1, Design), Color.Black, Color.Black, 0, default);
        large.Begin(camera, ViewLayout.Compute(new Vector2(2560, 1600), 2, Design), Color.Black, Color.Black, 0, default);

        AssertNear(new Rect2(0, 0, 640, 360), small.VisibleBounds);
        AssertNear(new Rect2(0, 0, 640, 360), large.VisibleBounds);
        Assert.Equal(4, large.Camera.Zoom);
        Assert.Equal(Matrix3x2.CreateScale(4), large.ScreenMatrix);
        Assert.Equal(new Rect2(0, 80, 2560, 1440), large.View.ViewRect);
    }

    [Fact]
    public void PixelSnapSnapsToDevicePixelsAtTheScaledZoom()
    {
        var frame = new RenderFrame();
        var layout = ViewLayout.Compute(new Vector2(1920, 1080), 1, Design with { IntegerScale = true });
        frame.Begin(new Camera2D(new Vector2(100.2f, 50.1f)) { PixelSnap = true }, layout, Color.Black, Color.Black, 0, default);

        var origin = Vector2.Transform(Vector2.Zero, frame.ViewMatrix);
        Assert.Equal(MathF.Round(origin.X), origin.X, 3);
        Assert.Equal(MathF.Round(origin.Y), origin.Y, 3);
    }

    private static readonly Vector2Comparer Tolerance = new(1e-3f);

    private static bool Covers(Vector2 outer, Vector2 inner) => outer.X >= inner.X - 1e-3f && outer.Y >= inner.Y - 1e-3f;

    private static bool Near(float a, float b) => MathF.Abs(a - b) < 1e-2f;

    private static void AssertNear(Rect2 expected, Rect2 actual) =>
        Assert.True(Near(expected.X, actual.X) && Near(expected.Y, actual.Y) && Near(expected.Width, actual.Width) && Near(expected.Height, actual.Height), $"Expected {expected}, got {actual}");

    private sealed class Vector2Comparer(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 x, Vector2 y) => Vector2.Distance(x, y) <= tolerance;

        public int GetHashCode(Vector2 obj) => 0;
    }
}
