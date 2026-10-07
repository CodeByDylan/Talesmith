using Talesmith.Diagnostics;

namespace Talesmith.Lighting;

/// <summary>Markers and counters recorded by the lighting systems on the game profiler.</summary>
public static class LightingMarkers
{
    public static readonly ProfilerMarker Lights = ProfilerMarker.Get("Lighting/Lights", "Lighting");
    public static readonly ProfilerMarker ShadowCasters = ProfilerMarker.Get("Lighting/Shadow casters", "Lighting");
    public static readonly ProfilerMarker TileOccluders = ProfilerMarker.Get("Lighting/Tile map occluders", "Lighting");
    public static readonly ProfilerMarker Emissive = ProfilerMarker.Get("Lighting/Emissive sprites", "Lighting");

    public static readonly ProfilerCounter LightsDrawn = ProfilerCounter.Get("Lights drawn", CounterKind.PerFrame, "lights");
    public static readonly ProfilerCounter ShadowedLights = ProfilerCounter.Get("Shadowed lights", CounterKind.PerFrame, "lights");
    public static readonly ProfilerCounter ShadowCastersDrawn = ProfilerCounter.Get("Shadow casters", CounterKind.PerFrame, "casters");
    public static readonly ProfilerCounter OccluderEdges = ProfilerCounter.Get("Occluder edges", CounterKind.PerFrame, "edges");
    public static readonly ProfilerCounter OccluderChunksBuilt = ProfilerCounter.Get("Occluder chunks built", CounterKind.PerFrame, "chunks");
    public static readonly ProfilerCounter EmissiveSprites = ProfilerCounter.Get("Emissive sprites", CounterKind.PerFrame, "sprites");
}
