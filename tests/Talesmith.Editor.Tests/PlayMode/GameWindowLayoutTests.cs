using Avalonia;
using Talesmith.Editor.PlayMode;

namespace Talesmith.Editor.Tests.PlayMode;

public sealed class GameWindowLayoutTests
{
    private const double Ring = 2;
    private const double Gap = 16;

    [Fact]
    public void WithoutAWindowTheContentFillsTheRoomInsideTheRing()
    {
        var layout = GameWindowLayout.Compute(new Size(800, 600), 1.5, null, null, Ring, Gap);

        Assert.Equal(new Size(796, 596), layout.ContentSize);
        Assert.Equal(1, layout.ContentScale);
        Assert.Equal(new Rect(2, 2, 796, 596), layout.Window);
        Assert.Equal(1, layout.Zoom);
        Assert.Equal(new Size(800, 600), layout.DesiredSize);
    }

    [Fact]
    public void AWindowLargerThanTheRoomFitsItCentered()
    {
        var layout = GameWindowLayout.Compute(new Size(1000, 700), 1, new GameWindowSize(1920, 1080), null, Ring, Gap);

        Assert.Equal(964.0 / 1920, layout.Zoom, 6);
        Assert.Equal(new Size(1920, 1080), layout.ContentSize);
        Assert.Equal(layout.Zoom, layout.ContentScale, 6);
        Assert.Equal(18, layout.Window.X);
        Assert.Equal(79, layout.Window.Y);
        Assert.Equal(964, layout.Window.Width, 6);
        Assert.Equal(542.25, layout.Window.Height, 6);
    }

    [Fact]
    public void FittingNeverEnlargesAWindowBeyondItsPixels()
    {
        var layout = GameWindowLayout.Compute(new Size(2000, 1500), 1, new GameWindowSize(1280, 720), null, Ring, Gap);

        Assert.Equal(1, layout.Zoom);
        Assert.Equal(new Rect(360, 390, 1280, 720), layout.Window);
    }

    [Fact]
    public void OnAHighDensityScreenAWindowShowsItsPixelsOneToOne()
    {
        var layout = GameWindowLayout.Compute(new Size(1000, 700), 2, new GameWindowSize(1920, 1080), null, Ring, Gap);

        Assert.Equal(1, layout.Zoom);
        Assert.Equal(new Size(1920, 1080), layout.ContentSize);
        Assert.Equal(0.5, layout.ContentScale);
        Assert.Equal(new Size(960, 540), layout.Window.Size);
    }

    [Fact]
    public void TheContentLaysOutInLogicalPixelsOfTheWindowsDisplay()
    {
        var layout = GameWindowLayout.Compute(new Size(2000, 2000), 1, new GameWindowSize(1170, 2532, 3), 0.5, Ring, Gap);

        Assert.Equal(new Size(390, 844), layout.ContentSize);
        Assert.Equal(1.5, layout.ContentScale);
        Assert.Equal(new Size(585, 1266), layout.Window.Size);
    }

    [Fact]
    public void AZoomedWindowLargerThanTheRoomStartsAtTheGapForScrolling()
    {
        var layout = GameWindowLayout.Compute(new Size(double.PositiveInfinity, double.PositiveInfinity), 1, new GameWindowSize(1920, 1080), 1, Ring, Gap);

        Assert.Equal(new Rect(18, 18, 1920, 1080), layout.Window);
        Assert.Equal(new Size(1956, 1116), layout.DesiredSize);
    }

    [Fact]
    public void TheWindowStartsOnWholeScreenPixels()
    {
        var layout = GameWindowLayout.Compute(new Size(1001, 701), 1.25, new GameWindowSize(800, 600), 1, Ring, Gap);

        Assert.Equal(Math.Round(layout.Window.X * 1.25), layout.Window.X * 1.25, 9);
        Assert.Equal(Math.Round(layout.Window.Y * 1.25), layout.Window.Y * 1.25, 9);
    }
}
