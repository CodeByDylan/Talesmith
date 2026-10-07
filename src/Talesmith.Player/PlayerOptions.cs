using System.Globalization;
using Microsoft.Extensions.Logging;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Player;

/// <summary>The player's command line.</summary>
internal sealed record PlayerOptions
{
    public const string Usage = """
        Usage: talesmith-player [game folder] [options]

        Plays the game in the folder (or its assets/ subfolder). Without a folder, an exported game's launcher.json next to the player
        names it; otherwise the current folder is played.

        Options:
          --renderer <auto|vulkan|skia>   Overrides the renderer from config/game.json.
          --benchmark                     Runs headlessly for a number of frames and prints a performance report.
          --frames <n>                    Frames to measure when benchmarking (default 600).
          --warmup <n>                    Frames to run before measuring (default 60).
          --size <width>x<height>         Resolution when benchmarking (default: the game's window size).
          --no-render                     Benchmarks the game loop only, without rendering frames.
          --report <path>                 Where the benchmark report is saved; .json or .csv (default captures/benchmark-<time>.json).
          --captures <folder>             Where F9 reports and F12 screenshots are saved (default ./captures).
          --no-dev-tools                  Disables the F3, F4, F9 and F12 developer tools.
          --mute                          Starts without sound.
          --opengl-compositor             Composites the window with OpenGL instead of Vulkan (Linux).
          --log-level <level>             trace, debug, information, warning, error or critical (default information).
          -h, --help                      Shows this help.
        """;

    public string GameDirectory { get; init; } = Environment.CurrentDirectory;

    public RendererPreference? Renderer { get; init; }

    public bool Benchmark { get; init; }

    public int Frames { get; init; } = 600;

    public int WarmupFrames { get; init; } = 60;

    /// <summary>The benchmark resolution, or null for the game's window size.</summary>
    public int? Width { get; init; }

    public int? Height { get; init; }

    public bool Render { get; init; } = true;

    public string? ReportPath { get; init; }

    public string CaptureDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "captures");

    public bool DeveloperTools { get; init; } = true;

    public bool Mute { get; init; }

    public bool VulkanCompositing { get; init; } = true;

    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    public bool ShowHelp { get; init; }

    /// <summary>The asset root: the game folder's assets/ subfolder when it has one, otherwise the game folder itself.</summary>
    public string AssetRoot
    {
        get
        {
            var assets = Path.Combine(GameDirectory, "assets");
            return Path.GetFullPath(Directory.Exists(assets) ? assets : GameDirectory);
        }
    }

    /// <summary>Parses arguments, returning null and a readable error when they are not valid.</summary>
    /// <param name="launcherFolder">Where to look for an exported game's <see cref="LauncherSettings"/>, by default the player's folder.</param>
    /// <exception cref="InvalidDataException">The launcher settings are not valid.</exception>
    public static PlayerOptions? Parse(IReadOnlyList<string> args, out string? error, string? launcherFolder = null)
    {
        launcherFolder ??= AppContext.BaseDirectory;
        var options = LauncherSettings.Find(launcherFolder) is { } launcher ? FromLauncher(launcher, launcherFolder) : new PlayerOptions();
        error = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    return options with { ShowHelp = true };
                case "--benchmark":
                    options = options with { Benchmark = true };
                    break;
                case "--no-render":
                    options = options with { Render = false };
                    break;
                case "--no-dev-tools":
                    options = options with { DeveloperTools = false };
                    break;
                case "--mute":
                    options = options with { Mute = true };
                    break;
                case "--opengl-compositor":
                    options = options with { VulkanCompositing = false };
                    break;
                case "--renderer" when Value(args, ref i) is { } value && Enum.TryParse<RendererPreference>(value, true, out var renderer):
                    options = options with { Renderer = renderer };
                    break;
                case "--frames" when PositiveNumber(args, ref i) is { } frames:
                    options = options with { Frames = frames };
                    break;
                case "--warmup" when Number(args, ref i) is { } warmup:
                    options = options with { WarmupFrames = warmup };
                    break;
                case "--size" when Size(Value(args, ref i)) is var (width, height):
                    options = options with { Width = width, Height = height };
                    break;
                case "--report" when Value(args, ref i) is { } report:
                    options = options with { ReportPath = report };
                    break;
                case "--captures" when Value(args, ref i) is { } captures:
                    options = options with { CaptureDirectory = Path.GetFullPath(captures) };
                    break;
                case "--log-level" when Value(args, ref i) is { } level && Enum.TryParse<LogLevel>(level, true, out var logLevel):
                    options = options with { LogLevel = logLevel };
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"'{arg}' is not a known option or is missing a valid value.";
                        return null;
                    }

                    options = options with { GameDirectory = Path.GetFullPath(arg) };
                    break;
            }
        }

        if (!Directory.Exists(options.GameDirectory))
        {
            error = $"The game folder {options.GameDirectory} does not exist.";
            return null;
        }

        return options;
    }

    private static PlayerOptions FromLauncher(LauncherSettings launcher, string folder)
    {
        var name = string.Join('_', (launcher.Title.Length > 0 ? launcher.Title : "Talesmith Game").Split(Path.GetInvalidFileNameChars()));
        return new PlayerOptions
        {
            GameDirectory = launcher.ResolveGameFolder(folder),
            LogLevel = launcher.LogLevel,
            DeveloperTools = launcher.DeveloperTools,
            CaptureDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), name, "captures")
        };
    }

    private static string? Value(IReadOnlyList<string> args, ref int index) => index + 1 < args.Count ? args[++index] : null;

    private static int? Number(IReadOnlyList<string> args, ref int index) =>
        int.TryParse(Value(args, ref index), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? PositiveNumber(IReadOnlyList<string> args, ref int index) => Number(args, ref index) is > 0 and var value ? value : null;

    private static (int Width, int Height)? Size(string? value)
    {
        var parts = value?.Split('x', 'X');
        if (parts is not [var w, var h]
            || !int.TryParse(w, NumberStyles.None, CultureInfo.InvariantCulture, out var width) || width <= 0
            || !int.TryParse(h, NumberStyles.None, CultureInfo.InvariantCulture, out var height) || height <= 0)
            return null;
        return (width, height);
    }
}
