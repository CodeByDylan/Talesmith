using Talesmith.Build.Packaging;

namespace Talesmith.Build.Pipeline.Steps;

/// <summary>Replaces the previous build with the new one and, for distribution builds, packs it into an archive.</summary>
internal sealed class FinishStep : IBuildStep
{
    public string Title => "Finishing";

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (Directory.Exists(context.BuildDirectory))
            Directory.Delete(context.BuildDirectory, recursive: true);
        Directory.Move(context.StagingDirectory, context.BuildDirectory);
        if (!context.Profile.Archive || context.Request.ContentOnly)
            return;

        var archive = ArchivePath(context);
        context.Progress(null, Path.GetFileName(archive));
        var executable = context.Target.ExecutableName(context.Name);
        await BuildArchive.WriteAsync(context.BuildDirectory, archive, context.Target,
            path => string.Equals(Path.GetFileName(path), executable, StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);
        context.Log(BuildLogLevel.Info, $"Packed {Path.GetFileName(archive)}: {new FileInfo(archive).Length / 1048576.0:N1} MB");
    }

    public static string ArchivePath(BuildContext context) =>
        Path.Combine(context.OutputRoot, $"{context.Name}-{context.Settings.Version}-{context.Target.RuntimeIdentifier}{BuildArchive.Extension(context.Target)}");

    /// <summary>The executable in the finished build.</summary>
    public static string ExecutablePath(BuildContext context) =>
        Path.Combine(context.BuildDirectory, context.Target.ExecutableName(context.Name));
}
