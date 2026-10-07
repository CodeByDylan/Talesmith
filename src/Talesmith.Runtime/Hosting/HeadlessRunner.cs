using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Diagnostics;
using Talesmith.Rendering;

namespace Talesmith.Runtime.Hosting;

/// <summary>Options for <see cref="HeadlessRunner"/>.</summary>
/// <param name="Frames">Frames to measure after warm-up.</param>
/// <param name="WarmupFrames">Frames run after the start scene loaded and before measuring, so caches fill and the JIT settles.</param>
/// <param name="FixedDeltaSeconds">Simulated time per frame, which makes runs reproducible; null uses real elapsed time.</param>
/// <param name="Renderer">Renders every frame offscreen when set, so rendering is measured too.</param>
/// <param name="OnFrame">Called after each measured frame, for example to move the camera through the map.</param>
public sealed record BenchmarkOptions(
    int Frames = 600,
    int WarmupFrames = 60,
    double? FixedDeltaSeconds = 1.0 / 60.0,
    IOffscreenRenderer? Renderer = null,
    int Width = 1280,
    int Height = 720,
    TimeSpan? LoadTimeout = null,
    Action<Game, long>? OnFrame = null);

/// <summary>Runs a game without a window for a number of frames and reports how it performed.</summary>
public static class HeadlessRunner
{
    private static readonly ProfilerMarker RenderFrameMarker = ProfilerMarker.Get("Render/Frame", "Rendering");

    /// <summary>Starts the game, waits for its start scene, warms up, then measures <see cref="BenchmarkOptions.Frames"/> frames.</summary>
    /// <remarks>Stops early when game code calls <see cref="GameLifetime.Quit"/>.</remarks>
    /// <exception cref="TimeoutException">The start scene did not load in time.</exception>
    public static ProfilerReport Run(Game game, BenchmarkOptions options)
    {
        game.Viewport.Size = new Vector2(options.Width, options.Height);
        game.Start();

        var clock = Stopwatch.StartNew();
        var timeout = options.LoadTimeout ?? TimeSpan.FromSeconds(60);
        while (game.Scenes.Current is null || game.Scenes.IsLoading)
        {
            if (clock.Elapsed > timeout)
                throw new TimeoutException($"The start scene did not load within {timeout.TotalSeconds:0} seconds.");
            Step(game, options, 1.0 / 60.0);
            Thread.Sleep(1);
        }

        var lifetime = game.Services.GetRequiredService<GameLifetime>();
        for (var i = 0; i < options.WarmupFrames && !lifetime.IsQuitRequested; i++)
            Step(game, options, options.FixedDeltaSeconds ?? 1.0 / 60.0);

        var previous = Stopwatch.GetTimestamp();
        long measured = 0;
        for (; measured < options.Frames && !lifetime.IsQuitRequested; measured++)
        {
            var now = Stopwatch.GetTimestamp();
            Step(game, options, options.FixedDeltaSeconds ?? Stopwatch.GetElapsedTime(previous, now).TotalSeconds);
            previous = now;
            options.OnFrame?.Invoke(game, measured);
        }

        var metadata = new Dictionary<string, string>
        {
            ["scene"] = game.Scenes.Current?.Request.ToString() ?? "none",
            ["renderer"] = options.Renderer is null ? "none" : game.Renderer.Info.Backend,
            ["device"] = game.Renderer.Info.Device,
            ["resolution"] = string.Create(CultureInfo.InvariantCulture, $"{options.Width}x{options.Height}"),
            ["view"] = ViewText(game.Viewport),
            ["frames"] = measured.ToString(CultureInfo.InvariantCulture),
            ["fixedDelta"] = options.FixedDeltaSeconds?.ToString(CultureInfo.InvariantCulture) ?? "real time"
        };
        return ProfilerReport.Capture(game.Profilers.All, (int)Math.Max(1, measured), metadata);
    }

    private static string ViewText(Viewport viewport)
    {
        var layout = viewport.Layout;
        var rect = layout.ViewRect;
        return string.Create(CultureInfo.InvariantCulture,
            $"{viewport.View.ScaleMode.ToString().ToLowerInvariant()} {layout.ViewSize.X:0.#}x{layout.ViewSize.Y:0.#} at {layout.Scale:0.##}x in {rect.Width:0}x{rect.Height:0}");
    }

    private static void Step(Game game, BenchmarkOptions options, double delta)
    {
        game.Tick(delta);
        if (options.Renderer is not { } renderer || game.Frames.BeginRead() is not { } frame)
            return;

        var profiler = game.Profilers.Render;
        profiler.BeginFrame();
        try
        {
            using (profiler.Measure(RenderFrameMarker))
                renderer.RenderOffscreen(frame, options.Width, options.Height);
        }
        finally
        {
            game.Frames.EndRead(frame);
            profiler.EndFrame();
        }
    }
}
