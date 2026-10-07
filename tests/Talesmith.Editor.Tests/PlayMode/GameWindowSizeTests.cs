using Talesmith.Editor.PlayMode;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Tests.PlayMode;

public sealed class GameWindowSizeTests
{
    [Theory]
    [InlineData(1920, 1080, "16:9")]
    [InlineData(1366, 768, "16:9")]
    [InlineData(1280, 800, "16:10")]
    [InlineData(1024, 768, "4:3")]
    [InlineData(2560, 1080, "21:9")]
    [InlineData(3440, 1440, "21:9")]
    [InlineData(1080, 1920, "9:16")]
    [InlineData(1170, 2532, "9:19.5")]
    [InlineData(1000, 700, "1.43:1")]
    [InlineData(700, 1000, "1:1.43")]
    public void TheAspectRatioHasItsCommonNameWhenItHasOne(int width, int height, string ratio) =>
        Assert.Equal(ratio, new GameWindowSize(width, height).AspectRatio);

    [Fact]
    public void ClampingKeepsSidesAndTheDisplayScaleInRange()
    {
        Assert.Equal(new GameWindowSize(64, 7680), new GameWindowSize(10, 100_000).Clamped());
        Assert.Equal(new GameWindowSize(1280, 720, 1.5), new GameWindowSize(1280, 720, 1.5).Clamped());
        Assert.Equal(new GameWindowSize(1280, 720), new GameWindowSize(1280, 720, 7).Clamped());
        Assert.Equal(new GameWindowSize(1280, 720), new GameWindowSize(1280, 720, double.NaN).Clamped());
    }

    [Fact]
    public void RotatingSwapsTheSidesAndKeepsTheDisplayScale() =>
        Assert.Equal(new GameWindowSize(2532, 1170, 3), new GameWindowSize(1170, 2532, 3).Rotated());

    [Fact]
    public void PresetsDescribeTheirSizeShapeAndDisplayScale()
    {
        Assert.Equal("1920 × 1080 · 16:9", GameWindowPreset.Common.Single(p => p.Name == "Full HD").Details);
        Assert.Equal("1170 × 2532 · 9:19.5 · 3×", GameWindowPreset.Common.Single(p => p.Name == "Phone").Details);
        Assert.Equal(GameWindowPreset.Common.Count, GameWindowPreset.Common.Select(p => p.Size).Distinct().Count());
    }

    [Fact]
    public void TheProjectsPresetsAreItsWindowAndAnotherDesignSizeOfAScaledView()
    {
        var unscaled = GameWindowPreset.Of(new GameSettings { WindowWidth = 1600, WindowHeight = 900 }).ToList();
        Assert.Equal([new GameWindowPreset("Game window", new GameWindowSize(1600, 900))], unscaled);

        var scaled = GameWindowPreset.Of(new GameSettings { View = new ViewSettings { Width = 320, Height = 180, ScaleMode = ViewScaleMode.Fit } }).ToList();
        Assert.Equal(["Game window", "Design size"], scaled.Select(p => p.Name));
        Assert.Equal(new GameWindowSize(320, 180), scaled[1].Size);

        var designed = new GameSettings { WindowWidth = 1280, WindowHeight = 800, View = new ViewSettings { Width = 1280, Height = 800 } };
        Assert.Equal(["Game window"], GameWindowPreset.Of(designed).Select(p => p.Name));

        var tiny = GameWindowPreset.Of(new GameSettings { WindowWidth = 0, WindowHeight = 20_000 }).Single();
        Assert.Equal(new GameWindowSize(64, 7680), tiny.Size);
    }
}
