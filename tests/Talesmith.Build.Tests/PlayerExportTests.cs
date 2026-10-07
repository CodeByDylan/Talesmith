using System.Diagnostics;
using Talesmith.Build.Player;

namespace Talesmith.Build.Tests;

/// <summary>Exports a game for this machine with a real published player and runs it headlessly.</summary>
/// <remarks>Slow: the first run publishes the player into the user's build cache, which takes a minute or more.</remarks>
[Trait("Category", "Slow")]
public sealed class PlayerExportTests
{
    [Fact]
    public async Task TheExportedGameStartsWithoutArgumentsAndRunsItsPackedSceneAndScripts()
    {
        Assert.SkipWhen(PublishedPlayerProvider.FindPlayerProject() is null, "The player's sources are not available.");
        using var project = TestProject.Create();
        var request = project.Request(BuildProfile.Development, contentOnly: false);

        var report = await new BuildPipeline().RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.True(File.Exists(report.Executable), report.Executable);
        Assert.Equal("Fixture Quest", Path.GetFileNameWithoutExtension(report.Executable));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(report.Executable)!, "launcher.json")));
        Assert.Contains(report.Categories, c => c.Name == "Engine and player" && c.Size > 10_000_000);

        var start = new ProcessStartInfo(report.Executable!)
        {
            WorkingDirectory = project.Folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "--benchmark", "--frames", "20", "--warmup", "5", "--no-render", "--report", Path.Combine(project.Folder, "benchmark.json") })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        var text = await output + await errors;

        Assert.True(process.ExitCode == 0, text);
        Assert.Contains("Scene scene(path=scenes/main.tscene) is active", text, StringComparison.Ordinal);
        Assert.Contains("Jumper started", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fail:", text, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project.Folder, "benchmark.json")));
    }

    [Fact]
    public async Task ThePublishPlayerCommandCopiesAPlayerForAnInstalledEditorToShip()
    {
        Assert.SkipWhen(PublishedPlayerProvider.FindPlayerProject() is null, "The player's sources are not available.");
        var folder = Directory.CreateTempSubdirectory("talesmith-players").FullName;
        try
        {
            var error = new StringWriter();

            var code = await BuildCommandLine.RunAsync(["--publish-player", folder, "--profile", "development"], TextWriter.Null, error,
                TestContext.Current.CancellationToken);

            Assert.True(code == 0, error.ToString());
            var player = Assert.Single(Directory.GetDirectories(folder));
            Assert.StartsWith(BuildTargets.Current.RuntimeIdentifier + "-", Path.GetFileName(player), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(player, BuildTargets.Current.PlayerExecutableName)));
            Assert.False(File.Exists(Path.Combine(player, ".complete")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
