using Talesmith.Diagnostics;

namespace Talesmith.Runtime.Diagnostics;

/// <summary>The engine's profilers: one for the game loop and one for the thread that renders.</summary>
public sealed class EngineProfilers
{
    /// <summary>Measures frames of the game loop: input, systems, frame building.</summary>
    public Profiler Game { get; } = new("Game");

    /// <summary>Measures frames on the render thread; render backends and hosts record into it.</summary>
    public Profiler Render { get; } = new("Render");

    public IReadOnlyList<Profiler> All => [Game, Render];
}

/// <summary>Markers and counters recorded by the runtime.</summary>
public static class RuntimeMarkers
{
    public static readonly ProfilerMarker Frame = ProfilerMarker.Get("Frame", "Engine");
    public static readonly ProfilerMarker Continuations = ProfilerMarker.Get("Engine/Continuations", "Engine");
    public static readonly ProfilerMarker Input = ProfilerMarker.Get("Engine/Input", "Engine");
    public static readonly ProfilerMarker Events = ProfilerMarker.Get("Engine/Events", "Engine");
    public static readonly ProfilerMarker Audio = ProfilerMarker.Get("Engine/Audio", "Engine");
    public static readonly ProfilerMarker Scheduling = ProfilerMarker.Get("Engine/Scheduling and tweens", "Engine");
    public static readonly ProfilerMarker PreUpdate = ProfilerMarker.Get("Phase/PreUpdate", "Phases");
    public static readonly ProfilerMarker FixedUpdate = ProfilerMarker.Get("Phase/FixedUpdate", "Phases");
    public static readonly ProfilerMarker Update = ProfilerMarker.Get("Phase/Update", "Phases");
    public static readonly ProfilerMarker LateUpdate = ProfilerMarker.Get("Phase/LateUpdate", "Phases");
    public static readonly ProfilerMarker PreRender = ProfilerMarker.Get("Phase/PreRender", "Phases");
    public static readonly ProfilerMarker Publish = ProfilerMarker.Get("Engine/Publish frame", "Engine");

    public static readonly ProfilerCounter Entities = ProfilerCounter.Get("Entities", CounterKind.Gauge, "entities");
    public static readonly ProfilerCounter Archetypes = ProfilerCounter.Get("Archetypes", CounterKind.Gauge, "archetypes");
    public static readonly ProfilerCounter FixedSteps = ProfilerCounter.Get("Fixed steps", CounterKind.PerFrame, "steps");
    public static readonly ProfilerCounter SpritesSubmitted = ProfilerCounter.Get("Sprites submitted", CounterKind.PerFrame, "sprites");
    public static readonly ProfilerCounter MeshInstancesSubmitted = ProfilerCounter.Get("Mesh instances submitted", CounterKind.PerFrame, "instances");
    public static readonly ProfilerCounter BatchesBuilt = ProfilerCounter.Get("Batches built", CounterKind.PerFrame, "batches");
    public static readonly ProfilerCounter VisibleChunks = ProfilerCounter.Get("Visible chunks", CounterKind.PerFrame, "chunks");
    public static readonly ProfilerCounter DecodedChunks = ProfilerCounter.Get("Decoded chunks", CounterKind.Gauge, "chunks");
    public static readonly ProfilerCounter ChunkMeshesBuilt = ProfilerCounter.Get("Chunk meshes built", CounterKind.PerFrame, "meshes");
    public static readonly ProfilerCounter AssetsLoaded = ProfilerCounter.Get("Assets loaded", CounterKind.Gauge, "assets");
    public static readonly ProfilerCounter AudioVoices = ProfilerCounter.Get("Audio voices", CounterKind.Gauge, "voices");
    public static readonly ProfilerCounter PendingContinuations = ProfilerCounter.Get("Pending continuations", CounterKind.Gauge, "tasks");
}
