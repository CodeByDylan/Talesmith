using System.Diagnostics;

namespace Talesmith.Scripting.Compiler.Tests;

public sealed class ToolingTests
{
    [Theory]
    [InlineData("PlayerController", null)]
    [InlineData("Enemy2", null)]
    [InlineData("", "Enter a class name.")]
    [InlineData("2Fast", "not a valid C# class name")]
    [InlineData("my-script", "not a valid C# class name")]
    [InlineData("class", "keyword")]
    [InlineData("Script", "engine type")]
    [InlineData("player", "capital letter, such as 'Player'")]
    public void ClassNamesAreValidated(string name, string? error)
    {
        var message = ScriptTemplates.ValidateClassName(name);

        if (error is null)
            Assert.Null(message);
        else
            Assert.Contains(error, message);
    }

    [Theory]
    [InlineData("isle-hopper", null, "IsleHopper")]
    [InlineData("Test Game", "Enemies/Bosses", "TestGame.Enemies.Bosses")]
    [InlineData("2d game", "ui", "_2dGame.Ui")]
    [InlineData("---", null, "Game")]
    public void NamespacesComeFromTheProjectAndFolders(string project, string? folder, string expected) =>
        Assert.Equal(expected, ScriptTemplates.NamespaceFor(project, folder));

    [Fact]
    public async Task EveryTemplateCompiles()
    {
        using var project = new TempProject();
        var script = ScriptTemplates.Create(project.Options, "", "PlayerController");
        ScriptTemplates.Create(project.Options, "Systems", "Regeneration", ScriptTemplate.System);
        ScriptTemplates.Create(project.Options, "Data", "Health", ScriptTemplate.Component);
        ScriptTemplates.Create(project.Options, Path.Combine(project.Options.SourceRoot, "Util"), "Helpers", ScriptTemplate.Class);
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Warning);
        Assert.Contains("namespace TestGame;", File.ReadAllText(script));
        Assert.Contains("TestGame.Systems.Regeneration", result.Load().SystemTypes.Select(t => t.FullName));
        Assert.Throws<IOException>(() => ScriptTemplates.Create(project.Options, "", "PlayerController"));
        Assert.Throws<ArgumentException>(() => ScriptTemplates.Create(project.Options, "../outside", "Escape"));
    }

    [Fact]
    public void TheProjectFileIsRewrittenOnlyWhenItChanges()
    {
        using var project = new TempProject();

        Assert.True(ScriptProjectFile.Write(project.Options));
        Assert.False(ScriptProjectFile.Write(project.Options));
        Assert.True(ScriptProjectFile.Write(project.Options with { AdditionalReferences = [typeof(ToolingTests).Assembly.Location] }));

        var path = ScriptProjectFile.PathFor(project.Options);
        Assert.Equal(Path.Combine(project.Directory, "Test Game.Scripts.csproj"), path);
        var content = File.ReadAllText(path);
        Assert.Contains("<HintPath>" + typeof(Script).Assembly.Location + "</HintPath>", content);
        Assert.Contains("<Compile Include=\"assets/scripts/**/*.cs\"", content);
        Assert.Contains("<Using Include=\"Talesmith.Scripting\" />", content);
        Assert.DoesNotContain("System.Private.CoreLib", content);
        Assert.DoesNotContain("<HintPath>" + typeof(ScriptCompiler).Assembly.Location, content);
        Assert.Contains("<Analyzer Include=\"" + typeof(ScriptCompiler).Assembly.Location + "\" />", content);
    }

    [Fact]
    public async Task TheGeneratedProjectBuildsWithDotnetBuild()
    {
        using var project = new TempProject();
        ScriptTemplates.Create(project.Options, "", "PlayerController");
        ScriptTemplates.Create(project.Options, "Systems", "Regeneration", ScriptTemplate.System);
        project.Write("Wasteful.cs", """
            namespace TestGame;

            public sealed class Wasteful : Script
            {
                protected override void Update()
                {
                    var hits = new List<Entity>();
                    Log.Debug("Hits: " + hits.Count);
                }
            }
            """);
        ScriptProjectFile.Write(project.Options);

        var start = new ProcessStartInfo("dotnet", ["build", ScriptProjectFile.PathFor(project.Options), "-nologo", "-v", "q"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = project.Directory
        };
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var build = Process.Start(start)!;
        var output = await build.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = await build.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await build.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.True(build.ExitCode == 0, output + errors);
        Assert.Contains("warning TS1002", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project.Directory, ".talesmith", "ide", "bin", ScriptAssembly.DefaultName + ".dll")), output);
    }

    [Fact]
    public async Task TheCompilationServiceRecompilesAfterChangesStop()
    {
        using var project = new TempProject();
        project.Write("Mover.cs", "namespace TestGame; public sealed class Mover : Script { }");
        using var service = new ScriptCompilationService(project.Options) { Debounce = TimeSpan.FromSeconds(1) };
        var started = 0;
        var finished = new List<ScriptCompilationFinishedEventArgs>();
        var second = new TaskCompletionSource<ScriptCompilationFinishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.CompilationStarted += (_, _) => Interlocked.Increment(ref started);
        service.CompilationFinished += (_, e) =>
        {
            lock (finished)
            {
                finished.Add(e);
                if (finished.Count == 2)
                    second.TrySetResult(e);
            }
        };

        await service.StartAsync(TestContext.Current.CancellationToken);
        project.Write("Jumper.cs", "namespace TestGame; public sealed class Jumper : Script { }");
        project.Write("Mover.cs", "namespace TestGame; public sealed class Mover : Script { public int Speed; }");
        var changed = await second.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.True(changed.Result.Success);
        Assert.Equal(["TestGame.Jumper", "TestGame.Mover"], changed.Assembly!.ScriptTypes.Select(t => t.FullName).Order());
        Assert.Same(changed.Assembly, service.Assembly);
        Assert.Equal(2, changed.Result.SourceFiles);
        Assert.Equal(2, started);
    }
}
