using Microsoft.Extensions.Logging;
using SkiaSharp;
using Talesmith.Diagnostics;

namespace Talesmith.Rendering.Skia;

/// <summary>Optional settings for a <see cref="SkiaRenderer"/>.</summary>
public sealed record SkiaRendererOptions
{
    /// <summary>The render thread's profiler, which receives the renderer's markers and counters.</summary>
    public Profiler? Profiler { get; init; }

    /// <summary>Receives shader compile errors; without one they go to <see cref="System.Diagnostics.Trace"/>.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>How textures created with <see cref="TextureFilter.Linear"/> are sampled; bilinear by default.</summary>
    /// <remarks>Textures have no mipmaps, so mipmap modes have no effect.</remarks>
    public SKSamplingOptions LinearSampling { get; init; } = new(SKFilterMode.Linear);
}
