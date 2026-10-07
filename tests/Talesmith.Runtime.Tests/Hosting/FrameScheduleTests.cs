using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Tests.Hosting;

/// <summary>The simulation thread's pacing on simulated time, where a second is 60,000 ticks and a 60 Hz refresh 1,000.</summary>
public sealed class FrameScheduleTests
{
    private const long Second = 60_000;
    private const long Refresh = Second / 60;

    [Fact]
    public void WithoutReportedRefreshesFramesFollowTheRefreshRate()
    {
        var frames = RunVSync(new FrameSchedule(Second, 0), Second, []);

        Assert.Equal(Every(Refresh, Refresh, Second), frames);
    }

    [Fact]
    public void AfterASecondWithoutReportedRefreshesFramesSlowDown()
    {
        var frames = RunVSync(new FrameSchedule(Second, 0), 3 * Second, []);

        var later = frames.Where(f => f > SimulationThread.UnattendedAfter.TotalSeconds * Second + Refresh).ToList();
        Assert.NotEmpty(later);
        Assert.All(Gaps(later), gap => Assert.Equal(Second / SimulationThread.UnattendedFramesPerSecond, gap));
    }

    [Fact]
    public void EachReportedRefreshRunsAFrame()
    {
        var reports = Every(Refresh, Refresh, Second);

        var frames = RunVSync(new FrameSchedule(Second, 0), Second, reports);

        Assert.Equal(reports, frames);
    }

    [Fact]
    public void FramesContinueAtTheRefreshRateWhileNoRefreshesAreReported()
    {
        var reports = Every(Refresh, Refresh, Second * 3 / 10).Concat(Every(Second * 8 / 10, Refresh, Second)).ToList();

        var frames = RunVSync(new FrameSchedule(Second, 0), Second, reports);

        var stall = frames.Where(f => f > Second * 3 / 10 && f < Second * 8 / 10).ToList();
        Assert.Equal(30, stall.Count);
        Assert.True(Gaps(frames).Max() <= Refresh * 3 / 2, $"The longest gap was {Gaps(frames).Max()} ticks.");
    }

    [Fact]
    public void AWakeUpLaterThanARefreshRunsOneFrameWithoutCatchingUp()
    {
        var late = Second / 10;

        var frames = RunVSync(new FrameSchedule(Second, 0), Second, [], lateness: late);

        Assert.Equal(Every(Refresh + late, Refresh + late, Second), frames);
    }

    [Fact]
    public void AWakeUpLateByLessThanARefreshKeepsTheCadence()
    {
        var frames = RunVSync(new FrameSchedule(Second, 0), Second / 2, [], lateness: Refresh / 4);

        Assert.Equal(Every(Refresh + Refresh / 4, Refresh, Second / 2), frames);
    }

    [Fact]
    public void AFrameRateCapSkipsReportedRefreshes()
    {
        var reports = Every(Refresh, Refresh, Second);

        var frames = RunVSync(new FrameSchedule(Second, 0), Second, reports, minimumSeconds: 1.0 / 30);

        Assert.Equal(Every(2 * Refresh, 2 * Refresh, Second), frames);
    }

    [Fact]
    public void WithoutVSyncFramesFollowTheFrameRateCap()
    {
        var cap = Second / 100;

        var frames = RunCapped(new FrameSchedule(Second, 0), Second, cap);

        Assert.Equal(Every(0, cap, Second), frames);
    }

    [Fact]
    public void WithoutVSyncALateFrameRunsTheNextOneAtOnceButNoMore()
    {
        var schedule = new FrameSchedule(Second, 0);
        var cap = Second / 100;
        schedule.FrameStarted(schedule.FrameDue(0), cap);

        var late = 5 * cap;
        schedule.FrameStarted(late, cap);

        Assert.Equal(late, schedule.FrameDue(late));
        schedule.FrameStarted(late, cap);
        Assert.Equal(late + cap, schedule.FrameDue(late));
    }

    [Fact]
    public void SwitchingToVSyncRestartsTheRefreshClock()
    {
        var schedule = new FrameSchedule(Second, 0);
        var now = 0L;
        for (var i = 0; i < 10; i++)
        {
            now = Math.Max(now, schedule.FrameDue(now));
            schedule.FrameStarted(now, Second / 100);
        }

        Assert.Equal(now + Refresh, schedule.RefreshDeadline(now, Refresh));
    }

    /// <summary>Runs the VSync loop of a simulation thread: each wait ends at the first report by the deadline, or at the deadline plus <paramref name="lateness"/>.</summary>
    /// <param name="reports">When refreshes are reported, in order; reports that arrive together count once.</param>
    private static List<long> RunVSync(FrameSchedule schedule, long until, List<long> reports, double minimumSeconds = 0, long lateness = 0)
    {
        var frames = new List<long>();
        var now = 0L;
        var next = 0;
        while (now < until)
        {
            var deadline = schedule.RefreshDeadline(now, Refresh);
            var reported = next < reports.Count && reports[next] <= deadline;
            if (reported)
            {
                now = Math.Max(now, reports[next]);
                while (next < reports.Count && reports[next] <= now)
                    next++;
            }
            else
                now = Math.Max(now, deadline) + lateness;

            if (now < until && schedule.RunsRefreshFrame(now, deadline, reported, Refresh, minimumSeconds))
                frames.Add(now);
        }

        return frames;
    }

    /// <summary>Runs the loop of a simulation thread without VSync, where each frame waits until it is due.</summary>
    private static List<long> RunCapped(FrameSchedule schedule, long until, long minimumTicks)
    {
        var frames = new List<long>();
        for (var now = schedule.FrameDue(0); now < until; now = Math.Max(now, schedule.FrameDue(now)))
        {
            frames.Add(now);
            schedule.FrameStarted(now, minimumTicks);
        }

        return frames;
    }

    private static List<long> Every(long from, long step, long until)
    {
        var times = new List<long>();
        for (var t = from; t < until; t += step)
            times.Add(t);
        return times;
    }

    private static List<long> Gaps(List<long> frames) => frames.Zip(frames.Skip(1), (a, b) => b - a).ToList();
}
