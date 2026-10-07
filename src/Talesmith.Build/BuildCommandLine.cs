using System.Diagnostics.CodeAnalysis;
using Talesmith.Build.Player;

namespace Talesmith.Build;

/// <summary>
/// Builds without the editor, as CI and scripts do: <c>talesmith --export</c> builds a project, and <c>talesmith --publish-player</c> publishes
/// a player for an installed editor to ship in its <c>players</c> folder.
/// </summary>
/// <remarks>Options left out of <c>--export</c> come from the project's build settings, as in the editor's Build panel.</remarks>
public static class BuildCommandLine
{
    public const string Usage =
        "talesmith --export <project folder> [--target <target>] [--profile <profile>] [--output <folder>] [--content-only]\n" +
        "       talesmith --publish-player <players folder> [--target <target>] [--profile <profile>]\n" +
        "Targets: win-x64, linux-x64. Profiles: development, release, distribution.";

    /// <summary>Whether the arguments ask for a build rather than the editor.</summary>
    public static bool Handles(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args is ["--export", ..] or ["--publish-player", ..];
    }

    /// <summary>Runs the command the arguments name, writing the build log to <paramref name="output"/> and problems to <paramref name="error"/>.</summary>
    /// <returns>0 when the command succeeded, 1 when it failed, and 2 when the arguments are not valid.</returns>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        var export = args is ["--export", ..];
        if (!TryParse(args, export, out var options, out var problem))
        {
            await error.WriteLineAsync(problem);
            await error.WriteLineAsync($"Usage: {Usage}");
            return 2;
        }

        var log = new LogWriter(output, error);
        return export
            ? await ExportAsync(options, log, output, error, cancellationToken)
            : await PublishPlayerAsync(options, log, output, error, cancellationToken);
    }

    private static async Task<int> ExportAsync(Options options, LogWriter log, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        BuildRequest request;
        try
        {
            request = BuildRequest.FromProject(options.Folder);
        }
        catch (InvalidDataException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 1;
        }

        request = request with
        {
            Target = options.Target ?? request.Target,
            Profile = options.Profile ?? request.Profile,
            OutputFolder = options.Output ?? request.OutputFolder,
            ContentOnly = options.ContentOnly
        };
        await output.WriteLineAsync($"Building {request.ProjectFolder} for {request.Target.DisplayName} ({request.Profile.DisplayName})");
        var report = await new BuildPipeline().RunAsync(request, log: log, cancellationToken: cancellationToken);
        if (!report.Success)
        {
            await error.WriteLineAsync($"The build failed with {report.Errors.Count} error(s).");
            return 1;
        }

        await output.WriteLineAsync($"Built {report.Executable ?? report.OutputDirectory}");
        if (report.Archive is { } archive)
            await output.WriteLineAsync($"Packed {archive}");
        return 0;
    }

    private static async Task<int> PublishPlayerAsync(Options options, LogWriter log, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var target = options.Target ?? BuildTargets.Current;
        var profile = options.Profile ?? BuildProfile.Release;
        string published;
        try
        {
            published = await new PublishedPlayerProvider().GetAsync(new PlayerRequest(target, profile), log, cancellationToken);
        }
        catch (BuildException ex)
        {
            await error.WriteLineAsync(ex.Message);
            return 1;
        }

        var destination = Path.Combine(Path.GetFullPath(options.Folder), $"{target.RuntimeIdentifier}-{profile.PlayerKey}");
        if (Directory.Exists(destination))
            Directory.Delete(destination, recursive: true);
        foreach (var file in Directory.EnumerateFiles(published, "*", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == PublishedPlayerProvider.CompleteMarker)
                continue;
            var copy = Path.Combine(destination, Path.GetRelativePath(published, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        await output.WriteLineAsync($"Published the {target.DisplayName} player ({profile.DisplayName}) to {destination}");
        return 0;
    }

    private static bool TryParse(IReadOnlyList<string> args, bool export, [NotNullWhen(true)] out Options? options, out string problem)
    {
        options = null;
        if (args is not [_, var folder, ..] || folder.StartsWith("--", StringComparison.Ordinal))
        {
            problem = export ? "Name the project folder to build." : "Name the folder to publish the player into.";
            return false;
        }

        if (export && !File.Exists(Path.Combine(folder, "assets", "config", "game.json")))
        {
            problem = $"{folder} is not a Talesmith project: it has no assets/config/game.json.";
            return false;
        }

        var parsed = new Options(folder);
        for (var i = 2; i < args.Count; i++)
        {
            var option = args[i];
            if (export && option == "--content-only")
            {
                parsed = parsed with { ContentOnly = true };
                continue;
            }

            if (option is not ("--target" or "--profile") && !(export && option == "--output"))
            {
                problem = $"Unknown option {option}.";
                return false;
            }

            if (i + 1 >= args.Count)
            {
                problem = $"{option} needs a value.";
                return false;
            }

            var value = args[++i];
            switch (option)
            {
                case "--target" when BuildTargets.Find(value) is { } target:
                    parsed = parsed with { Target = target };
                    break;
                case "--target":
                    problem = $"Unknown target {value}. Choose one of {string.Join(", ", BuildTargets.All.Select(t => t.RuntimeIdentifier))}.";
                    return false;
                case "--profile" when Enum.TryParse<BuildProfileKind>(value, ignoreCase: true, out var kind) && Enum.IsDefined(kind):
                    parsed = parsed with { Profile = BuildProfile.For(kind) };
                    break;
                case "--profile":
                    problem = $"Unknown profile {value}. Choose one of {string.Join(", ", BuildProfile.All.Select(p => p.DisplayName.ToLowerInvariant()))}.";
                    return false;
                default:
                    parsed = parsed with { Output = Path.GetFullPath(value) };
                    break;
            }
        }

        options = parsed;
        problem = "";
        return true;
    }

    private sealed record Options(string Folder)
    {
        public BuildTarget? Target { get; init; }

        public BuildProfile? Profile { get; init; }

        public string? Output { get; init; }

        public bool ContentOnly { get; init; }
    }

    /// <summary>Writes each log entry as it arrives, warnings and errors to the error writer.</summary>
    private sealed class LogWriter(TextWriter output, TextWriter error) : IProgress<BuildLogEntry>, IBuildOutput
    {
        private readonly Lock _lock = new();

        public void Report(BuildLogEntry value)
        {
            if (value.Level == BuildLogLevel.Debug)
                return;
            lock (_lock)
            {
                if (value.Level >= BuildLogLevel.Warning)
                    error.WriteLine($"{value.Level.ToString().ToLowerInvariant()}: {value}");
                else
                    output.WriteLine(value);
            }
        }

        public void Log(BuildLogLevel level, string message) => Report(new BuildLogEntry(DateTimeOffset.Now, level, message));

        public void Progress(double? fraction, string? detail = null)
        {
        }
    }
}
