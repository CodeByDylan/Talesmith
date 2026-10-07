using Talesmith.Build.Player;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Build.Pipeline.Steps;

/// <summary>Gets the published player for the target, publishing it the first time.</summary>
internal sealed class PreparePlayerStep : IBuildStep
{
    public string Title => "Preparing the player";

    public bool AppliesTo(BuildContext context) => !context.Request.ContentOnly;

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var provider = context.Request.Player ?? new PublishedPlayerProvider();
        var request = new PlayerRequest(context.Target, context.Profile) { Title = context.Game.Title, Version = context.Settings.Version, Icon = context.Icon };
        context.PlayerDirectory = await provider.GetAsync(request, context, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Puts the player next to the game under the game's name, with the launcher settings and the platform's own files.</summary>
internal sealed class AssembleStep : IBuildStep
{
    public const string PlayerCategory = BuildReport.PlayerCategory;

    public string Title => "Assembling the build";

    public bool AppliesTo(BuildContext context) => !context.Request.ContentOnly;

    public async Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var player = context.PlayerDirectory ?? throw new InvalidOperationException("The player must be prepared before the build is assembled.");
        var executableFolder = context.StagingDirectory;
        Directory.CreateDirectory(executableFolder);
        foreach (var file in Directory.EnumerateFiles(player, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(player, file);
            if (Path.GetFileName(relative).StartsWith('.'))
                continue;
            var name = string.Equals(relative, context.Target.PlayerExecutableName, StringComparison.Ordinal) ? context.Target.ExecutableName(context.Name) : relative;
            var destination = Path.Combine(executableFolder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            context.AddSize(PlayerCategory, new FileInfo(destination).Length, new FileInfo(destination).Length);
        }

        var executable = Path.Combine(executableFolder, context.Target.ExecutableName(context.Name));
        if (!OperatingSystem.IsWindows() && context.Target.Platform != BuildPlatform.Windows)
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

        new LauncherSettings
        {
            GameFolder = Path.GetRelativePath(executableFolder, context.StagingGameDirectory).Replace('\\', '/'),
            Title = context.Game.Title,
            LogLevel = context.Profile.LogLevel,
            DeveloperTools = context.Profile.DeveloperTools
        }.Save(executableFolder);
        await context.Layout.FinishAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
