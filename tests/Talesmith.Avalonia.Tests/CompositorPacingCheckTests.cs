using System.Diagnostics;
using Talesmith.Avalonia.Presentation;

namespace Talesmith.Avalonia.Tests;

public sealed class CompositorPacingCheckTests
{
    /// <summary>The animation frames the check needs before it decides.</summary>
    private const int Needed = CompositorPacingCheck.WarmupFrames + CompositorPacingCheck.Frames;

    [Theory]
    [InlineData(160)]
    [InlineData(146.5)]
    public void AnimationFramesFasterThanTheDisplayAreReportedOnce(double framesPerSecond)
    {
        var check = new CompositorPacingCheck(() => 143.5);

        var reports = Feed(check, Enumerable.Repeat(1 / framesPerSecond, Needed + 50));

        Assert.Equal(1, reports);
        Assert.True(check.IsComplete);
        Assert.Equal(framesPerSecond, check.FrameRate, 0.5);
        Assert.Equal(143.5, check.DisplayRate);
    }

    [Theory]
    [InlineData(143.5)]
    [InlineData(144.5)]
    [InlineData(120)]
    public void AnimationFramesThatKeepUpWithTheDisplayAreFine(double framesPerSecond)
    {
        var check = new CompositorPacingCheck(() => 143.5);

        Assert.Equal(0, Feed(check, Enumerable.Repeat(1 / framesPerSecond, Needed)));
        Assert.True(check.IsComplete);
    }

    [Fact]
    public void LoadingAndHitchesDoNotHideACompositorThatRunsTooFast()
    {
        var check = new CompositorPacingCheck(() => 143.5);
        var loading = Enumerable.Repeat(0.25, CompositorPacingCheck.WarmupFrames);
        var hitchy = Enumerable.Range(0, CompositorPacingCheck.Frames).Select(i => i % 8 == 0 ? 0.05 : 1 / 160.0);

        Assert.Equal(1, Feed(check, loading.Concat(hitchy)));
        Assert.Equal(160, check.FrameRate, 0.5);
    }

    [Fact]
    public void AnUnknownDisplayRateIsNeverReported()
    {
        var check = new CompositorPacingCheck(() => null);

        Assert.Equal(0, Feed(check, Enumerable.Repeat(1 / 300.0, Needed)));
        Assert.True(check.IsComplete);
        Assert.Null(check.DisplayRate);
    }

    [Fact]
    public void NothingIsDecidedBeforeEnoughFramesArrived()
    {
        var asked = false;
        var check = new CompositorPacingCheck(() =>
        {
            asked = true;
            return 60;
        });

        Assert.Equal(0, Feed(check, Enumerable.Repeat(1 / 500.0, Needed - 1)));
        Assert.False(check.IsComplete);
        Assert.False(asked);
    }

    /// <summary>Observes animation frames separated by the given seconds and counts how often the check reported a compositor that runs too fast.</summary>
    private static int Feed(CompositorPacingCheck check, IEnumerable<double> intervals)
    {
        var reports = 0;
        var time = (double)Stopwatch.GetTimestamp();
        foreach (var interval in intervals)
        {
            time += interval * Stopwatch.Frequency;
            if (check.Observe((long)time))
                reports++;
        }

        return reports;
    }
}
