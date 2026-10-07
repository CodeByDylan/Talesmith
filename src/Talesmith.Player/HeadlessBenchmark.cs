using System.Globalization;
using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Player;

/// <summary>Runs a game without a window, prints its performance report and saves it.</summary>
internal static class HeadlessBenchmark
{
    public static int Run(PlayerOptions options)
    {
        using var loggers = LoggerFactory.Create(logging => logging
            .SetMinimumLevel(options.LogLevel)
            .AddSimpleConsole(console => console.SingleLine = true));
        var session = GameSession.Create(new DesktopGameOptions
        {
            AssetRoot = options.AssetRoot,
            Renderer = options.Renderer,
            Audio = false,
            DeveloperTools = false
        }, loggers, WindowGraphics.None);

        try
        {
            var benchmark = new BenchmarkOptions(
                Frames: options.Frames,
                WarmupFrames: options.WarmupFrames,
                Renderer: options.Render ? session.Backend.Renderer as IOffscreenRenderer : null,
                Width: options.Width ?? session.Game.Settings.WindowWidth,
                Height: options.Height ?? session.Game.Settings.WindowHeight);
            var report = HeadlessRunner.Run(session.Game, benchmark);
            Console.WriteLine(report.ToText());

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var path = report.Save(options.ReportPath ?? Path.Combine(options.CaptureDirectory, $"benchmark-{stamp}.json"));
            Console.WriteLine($"Report saved to {path}");
            return 0;
        }
        finally
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
