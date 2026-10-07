using Talesmith.Runtime.Scheduling;

namespace Talesmith.Runtime.Tests;

public sealed class GameSchedulerTests
{
    [Fact]
    public void DisposingARepeatTwiceStopsItOnce()
    {
        var scheduler = new GameScheduler();
        var calls = 0;
        var repeat = scheduler.Every(0.1, () => calls++);
        scheduler.Update(0.15, 0.15);

        repeat.Dispose();
        repeat.Dispose();
        scheduler.Update(0.5, 0.5);

        Assert.Equal(1, calls);
    }
}
