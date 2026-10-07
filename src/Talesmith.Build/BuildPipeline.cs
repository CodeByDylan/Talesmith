using System.Diagnostics;
using Talesmith.Assets;
using Talesmith.Build.Packaging;
using Talesmith.Build.Pipeline;
using Talesmith.Build.Pipeline.Steps;
using Talesmith.Build.Player;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Build;

/// <summary>Exports a game: checks the project, compiles scripts, collects and packs content, adds the player and packages the result.</summary>
/// <remarks>
/// <para>Steps run on the thread pool one after another; the build stops after the first step that logs errors. Progress and log entries are
/// reported through <see cref="IProgress{T}"/>, so a <see cref="Progress{T}"/> created on the UI thread receives them there.</para>
/// <para>The build is assembled in a hidden staging folder and replaces the previous build of the same target only when every step
/// succeeded. The report is saved next to the build folder as <c>&lt;name&gt;-&lt;rid&gt;.report.json</c>, also for failed builds.</para>
/// </remarks>
public sealed class BuildPipeline
{
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(50);

    public BuildPipeline()
        : this([new CheckProjectStep(), new CompileScriptsStep(), new CollectContentStep(), new WriteGameStep(), new PreparePlayerStep(), new AssembleStep(), new FinishStep()])
    {
    }

    public BuildPipeline(IEnumerable<IBuildStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        Steps = [.. steps];
    }

    public IReadOnlyList<IBuildStep> Steps { get; }

    public async Task<BuildReport> RunAsync(BuildRequest request, IProgress<BuildProgress>? progress = null, IProgress<BuildLogEntry>? log = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var startedAt = DateTimeOffset.Now;
        var clock = Stopwatch.StartNew();
        GameSettings game;
        try
        {
            game = GameSettings.Load(request.AssetRoot);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            var error = new BuildLogEntry(DateTimeOffset.Now, BuildLogLevel.Error, ex.Message) { File = GameSettings.FileName };
            log?.Report(error);
            return new BuildReport
            {
                Game = Path.GetFileName(request.ProjectFolder),
                Target = request.Target.RuntimeIdentifier,
                Profile = request.Profile.Kind,
                StartedAt = startedAt,
                Duration = clock.Elapsed,
                Errors = [error]
            };
        }

        var context = new BuildContext(request, game, PlatformLayouts.For(request.Target), log);
        var steps = Steps.Where(s => s.AppliesTo(context)).ToArray();
        var timings = new List<BuildStepTiming>();
        var cancelled = false;
        context.Log(BuildLogLevel.Info, $"Building {game.Title} for {request.Target.DisplayName} ({request.Profile.DisplayName})");
        try
        {
            for (var i = 0; i < steps.Length && !context.HasErrors; i++)
            {
                var step = steps[i];
                var index = i;
                TimeSpan? lastReport = null;
                context.CurrentStep = step.Title;
                context.ProgressHandler = (fraction, detail) =>
                {
                    var now = clock.Elapsed;
                    if (fraction is > 0 and < 1 && now - lastReport < ProgressInterval)
                        return;
                    lastReport = now;
                    progress?.Report(new BuildProgress(index, steps.Length, step.Title, fraction, detail));
                };
                progress?.Report(new BuildProgress(i, steps.Length, step.Title));
                var stepClock = Stopwatch.StartNew();
                try
                {
                    await Task.Run(() => step.ExecuteAsync(context, cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    context.Log(BuildLogLevel.Warning, "The build was cancelled");
                    break;
                }
                catch (Exception ex) when (ex is BuildException or IOException or UnauthorizedAccessException or InvalidDataException or AssetException)
                {
                    context.Log(BuildLogLevel.Error, ex.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.Log(BuildLogLevel.Error, $"{step.Title} failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
                }

                timings.Add(new BuildStepTiming(step.Title, stepClock.Elapsed));
            }
        }
        finally
        {
            if (request.Assets is null && context.Assets is { } owned)
                await owned.DisposeAsync().ConfigureAwait(false);
            if (Directory.Exists(context.StagingDirectory))
                TryDelete(context.StagingDirectory);
        }

        var success = !cancelled && !context.HasErrors;
        progress?.Report(new BuildProgress(steps.Length, steps.Length, success ? "Done" : cancelled ? "Cancelled" : "Failed", 1));
        context.Log(success ? BuildLogLevel.Info : BuildLogLevel.Error,
            success ? $"Built {game.Title} in {clock.Elapsed.TotalSeconds:N1} s" : cancelled ? "Cancelled" : $"The build failed with {context.Errors.Count} {(context.Errors.Count == 1 ? "error" : "errors")}");

        var report = new BuildReport
        {
            Success = success,
            Cancelled = cancelled,
            Game = game.Title,
            Target = request.Target.RuntimeIdentifier,
            Profile = request.Profile.Kind,
            StartedAt = startedAt,
            Duration = clock.Elapsed,
            OutputDirectory = success ? context.BuildDirectory : null,
            Executable = success && !request.ContentOnly ? FinishStep.ExecutablePath(context) : null,
            Archive = success && request.Profile.Archive && !request.ContentOnly ? FinishStep.ArchivePath(context) : null,
            AssetCount = context.Content?.Items.Count ?? 0,
            Categories = [.. context.Sizes.Values.OrderByDescending(c => c.Size)],
            LargestAssets = [.. context.AssetSizes.OrderByDescending(a => a.StoredSize).Take(15)],
            Steps = timings,
            Warnings = context.Warnings,
            Errors = context.Errors
        };
        return await SaveAsync(report, context).ConfigureAwait(false);
    }

    private static async Task<BuildReport> SaveAsync(BuildReport report, BuildContext context)
    {
        try
        {
            Directory.CreateDirectory(context.OutputRoot);
            var path = Path.Combine(context.OutputRoot, $"{context.Name}-{context.Target.RuntimeIdentifier}{BuildReport.FileSuffix}");
            report = report with { ReportFile = path };
            await File.WriteAllTextAsync(path, report.ToJson()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Log(BuildLogLevel.Warning, $"The build report could not be saved: {ex.Message}");
        }

        return report;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
