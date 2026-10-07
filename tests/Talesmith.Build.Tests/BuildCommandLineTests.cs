namespace Talesmith.Build.Tests;

public sealed class BuildCommandLineTests
{
    [Fact]
    public async Task ExportsAProjectForTheGivenTargetAndProfileIntoTheOutputFolder()
    {
        using var project = TestProject.Create();
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await BuildCommandLine.RunAsync(
            ["--export", project.Folder, "--target", "linux-x64", "--profile", "development", "--output", project.Output, "--content-only"], output, error,
            TestContext.Current.CancellationToken);

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(project.Output, "Fixture Quest-linux-x64", "game", "content.tspack")), error.ToString());
        Assert.Contains("for Linux x64 (Development)", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Built ", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptErrorsFailTheExportWithTheirLocation()
    {
        using var project = TestProject.Create("""
            namespace Fixture;

            public sealed class Broken : Script
            {
                protected override void Update() => Missing();
            }
            """);
        var error = new StringWriter();

        var code = await BuildCommandLine.RunAsync(["--export", project.Folder, "--output", project.Output, "--content-only"], TextWriter.Null, error,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, code);
        Assert.Contains("Jumper.cs(5,", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("CS0103", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("The build failed", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--export", "Name the project folder")]
    [InlineData("--export {project}/missing", "is not a Talesmith project")]
    [InlineData("--export {project} --target amiga", "Unknown target amiga. Choose one of win-x64, linux-x64.")]
    [InlineData("--export {project} --profile fast", "Unknown profile fast. Choose one of development, release, distribution.")]
    [InlineData("--export {project} --output", "--output needs a value.")]
    [InlineData("--export {project} --verbose", "Unknown option --verbose.")]
    [InlineData("--publish-player", "Name the folder to publish the player into.")]
    [InlineData("--publish-player {project} --output out", "Unknown option --output.")]
    [InlineData("--publish-player {project} --content-only", "Unknown option --content-only.")]
    [InlineData("--publish-player {project} --target amiga", "Unknown target amiga.")]
    public async Task InvalidArgumentsExplainTheProblemAndPrintTheUsage(string arguments, string problem)
    {
        using var project = TestProject.Create();
        var error = new StringWriter();
        string[] args = [.. arguments.Split(' ').Select(a => a.Replace("{project}", project.Folder, StringComparison.Ordinal))];

        var code = await BuildCommandLine.RunAsync(args, TextWriter.Null, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, code);
        Assert.Contains(problem, error.ToString(), StringComparison.Ordinal);
        Assert.Contains($"Usage: {BuildCommandLine.Usage}", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheExportAndPublishPlayerOptionsAskForABuild()
    {
        Assert.True(BuildCommandLine.Handles(["--export", "game"]));
        Assert.True(BuildCommandLine.Handles(["--publish-player", "players"]));
        Assert.False(BuildCommandLine.Handles(["--new", "platformer", "projects", "Game"]));
        Assert.False(BuildCommandLine.Handles(["game"]));
        Assert.False(BuildCommandLine.Handles([]));
    }
}
