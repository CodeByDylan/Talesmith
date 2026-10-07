using Talesmith.Diagnostics;

namespace Talesmith.Physics;

/// <summary>Markers and counters recorded by the physics world, shown in the performance overlay and reports.</summary>
public static class PhysicsCounters
{
    public static readonly ProfilerMarker Sync = ProfilerMarker.Get("Physics/Sync", "Physics");
    public static readonly ProfilerMarker Broadphase = ProfilerMarker.Get("Physics/Broadphase", "Physics");
    public static readonly ProfilerMarker Narrowphase = ProfilerMarker.Get("Physics/Narrowphase", "Physics");
    public static readonly ProfilerMarker Solve = ProfilerMarker.Get("Physics/Solve", "Physics");
    public static readonly ProfilerMarker Continuous = ProfilerMarker.Get("Physics/Continuous", "Physics");
    public static readonly ProfilerMarker Events = ProfilerMarker.Get("Physics/Events", "Physics");

    public static readonly ProfilerCounter Bodies = ProfilerCounter.Get("Physics bodies", CounterKind.Gauge, "bodies");
    public static readonly ProfilerCounter AwakeBodies = ProfilerCounter.Get("Physics awake bodies", CounterKind.Gauge, "bodies");
    public static readonly ProfilerCounter Contacts = ProfilerCounter.Get("Physics contacts", CounterKind.Gauge, "contacts");
}
