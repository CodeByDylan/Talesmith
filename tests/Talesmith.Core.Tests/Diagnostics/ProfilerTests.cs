using Talesmith.Diagnostics;

namespace Talesmith.Core.Tests.Diagnostics;

public sealed class ProfilerTests
{
    private static readonly ProfilerMarker Section = ProfilerMarker.Get("Tests/Section", "Tests");
    private static readonly ProfilerCounter Items = ProfilerCounter.Get("Tests/Items", CounterKind.Gauge);

    [Fact]
    public void SectionsAreNotChargedForMarkersRegisteredDuringTheFrame()
    {
        var profiler = new Profiler("Test");
        RecordFrame(profiler);

        profiler.BeginFrame();
        ProfilerMarker.Get($"Tests/Registered elsewhere {Guid.NewGuid()}", "Tests");
        ProfilerCounter.Get($"Tests/Registered elsewhere {Guid.NewGuid()}");
        using (profiler.Measure(Section))
        {
            profiler.Set(Items, 3);
            profiler.Increment(Items);
        }

        profiler.EndFrame();

        Assert.Equal(0, profiler.LastFrame!.MarkerAllocatedBytes(Section));
        Assert.Equal(4, profiler.LastFrame.CounterValue(Items));
    }

    private static void RecordFrame(Profiler profiler)
    {
        profiler.BeginFrame();
        using (profiler.Measure(Section))
        {
            profiler.Set(Items, 1);
            profiler.Increment(Items);
        }

        profiler.EndFrame();
    }
}
