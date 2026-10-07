using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Talesmith.Avalonia.Presentation;
using Talesmith.Diagnostics;
using Talesmith.Imaging;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Diagnostics;

/// <summary>Developer actions behind the host's hotkeys: profiler reports, screenshots and debug views.</summary>
/// <remarks>Safe to use from the UI thread while a simulation thread runs the game: game state is read and changed on the game thread.</remarks>
/// <param name="captureDirectory">Where reports and screenshots are saved.</param>
public sealed class DeveloperTools(Game game, IGamePresenter presenter, string captureDirectory)
{
    /// <summary>The number of recent frames a saved report covers.</summary>
    public const int ReportFrames = 600;

    private readonly DebugOptions _debug = game.Services.GetRequiredService<DebugOptions>();

    public string CaptureDirectory { get; } = Path.GetFullPath(captureDirectory);

    /// <summary>Saves the recent frames of every profiler as JSON and CSV and returns the JSON path.</summary>
    public async Task<string> SaveProfilerReportAsync()
    {
        var metadata = await game.InvokeAsync(Metadata).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var report = ProfilerReport.Capture(game.Profilers.All, ReportFrames, metadata);
            var name = Path.Combine(CaptureDirectory, $"profile-{Timestamp()}");
            report.Save(name + ".csv");
            return report.Save(name + ".json");
        }).ConfigureAwait(false);
    }

    /// <summary>Saves the next rendered frame as a PNG and returns its path.</summary>
    public async Task<string> SaveScreenshotAsync()
    {
        var image = await presenter.CaptureAsync().ConfigureAwait(false);
        var path = Path.Combine(CaptureDirectory, $"screenshot-{Timestamp()}.png");
        Directory.CreateDirectory(CaptureDirectory);
        await File.WriteAllBytesAsync(path, EncodePng(image)).ConfigureAwait(false);
        return path;
    }

    /// <summary>Switches to the next combination of debug views and describes it.</summary>
    public Task<string> CycleDebugViewsAsync() => game.InvokeAsync(CycleDebugViews);

    private string CycleDebugViews()
    {
        (_debug.ShowGrid, _debug.ShowChunks, _debug.ShowObjects) = (_debug.ShowGrid, _debug.ShowChunks, _debug.ShowObjects) switch
        {
            (false, false, false) => (true, false, false),
            (true, false, false) => (true, true, false),
            (true, true, false) => (true, true, true),
            _ => (false, false, false)
        };

        if (!_debug.Any)
            return "Debug views off";
        List<string> views = [];
        if (_debug.ShowGrid)
            views.Add("grid");
        if (_debug.ShowChunks)
            views.Add("chunks");
        if (_debug.ShowObjects)
            views.Add("objects");
        return "Debug views: " + string.Join(", ", views);
    }

    private Dictionary<string, string> Metadata() => new()
    {
        ["game"] = game.Settings.Title,
        ["scene"] = game.Scenes.Current?.Request.ToString() ?? "none",
        ["renderer"] = game.Renderer.Info.Backend,
        ["device"] = game.Renderer.Info.Device,
        ["resolution"] = string.Create(CultureInfo.InvariantCulture, $"{game.Viewport.Size.X:0}x{game.Viewport.Size.Y:0}")
    };

    private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static byte[] EncodePng(ImageData image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        image.Pixels.AsSpan(0, info.BytesSize).CopyTo(bitmap.GetPixelSpan());
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
