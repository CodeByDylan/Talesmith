using Talesmith.Assets;
using Talesmith.Assets.Packs;
using Talesmith.Build.Content;
using Talesmith.Build.Pipeline;

namespace Talesmith.Build.Tests;

public sealed class BuildPipelineTests
{
    [Fact]
    public async Task ShipsExactlyTheAssetsReachableFromScenesCodeAndAlwaysInclude()
    {
        using var project = TestProject.Create();
        var request = project.Request();
        request = request with { Settings = request.Settings with { AlwaysInclude = ["ui/menu", "label:shipped", "missing/folder"] } };

        var report = await new BuildPipeline().RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var pack = new PackAssetSource(Path.Combine(report.OutputDirectory!, "game", ContentPack.FileName));
        string[] expected =
        [
            AssetIndex.FileName, "audio/jump.wav", "fx/glow.png", "prefabs/coin.tprefab", "scenes/level2.tscene", "scenes/main.tscene",
            "sprites/coin.png", "sprites/hero.png", "ui/menu/button.png", "ui/menu/frame.png"
        ];
        Assert.Equal(expected, pack.Entries.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.Contains(report.Warnings, w => w.Message.Contains("missing/folder", StringComparison.Ordinal));
        Assert.Equal(9, report.AssetCount);
        Assert.Contains(report.Categories, c => c.Name == "Texture" && c.Files == 5);
    }

    [Fact]
    public async Task LeavesEditorFilesOutAndShipsConfigurationAndScriptsLoose()
    {
        using var project = TestProject.Create();
        var request = project.Request();

        var report = await new BuildPipeline().RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var build = report.OutputDirectory!;
        Assert.Equal(Path.Combine(project.Output, $"Fixture Quest-{request.Target.RuntimeIdentifier}"), build);
        var files = Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(build, f).Replace('\\', '/')).Order().ToArray();
        Assert.Equal(["game/config/game.json", "game/config/input.json", "game/content.tspack", "game/scripts/bin/Game.Scripts.dll"], files);
        Assert.True(File.Exists(report.ReportFile));

        var catalog = AssetCatalog.Load(PackAssetSource.OpenFolder(Path.Combine(build, "game")));
        var heroMeta = AssetMetaFile.Read(File.OpenRead(Path.Combine(project.AssetRoot, "sprites/hero.png.meta")));
        Assert.True(catalog.TryGetPath(heroMeta.Guid, out var heroPath));
        Assert.Equal("sprites/hero.png", heroPath);
        Assert.True(catalog.TryGetGuid("sprites", out _));
        Assert.False(catalog.TryGetGuid("sprites/unused.png", out _));
    }

    [Fact]
    public async Task PluginsShipWithoutMetaFilesAndTheBuildWritesNoneIntoThem()
    {
        using var project = TestProject.Create();
        project.WritePlugin("weather", "fixture.weather");
        project.WriteText("plugins/weather/plugin.json.meta", """{ "version": 1, "guid": "00112233445566778899aabbccddeeff" }""");

        var report = await new BuildPipeline().RunAsync(project.Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var shipped = Path.Combine(report.OutputDirectory!, "game", "plugins");
        Assert.Equal(["weather/Fixture.weather.dll", "weather/plugin.json"],
            Directory.EnumerateFiles(shipped, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(shipped, f).Replace('\\', '/')).Order(StringComparer.Ordinal));
        Assert.Equal([Path.Combine(project.AssetRoot, "plugins", "weather", "plugin.json.meta")],
            Directory.EnumerateFiles(Path.Combine(project.AssetRoot, "plugins"), "*.meta", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(project.AssetRoot, "plugins.meta")));
    }

    [Fact]
    public async Task ScriptErrorsFailTheBuildWithTheirLocation()
    {
        using var project = TestProject.Create("""
            namespace Fixture;

            public sealed class Broken : Script
            {
                protected override void Update() => Missing();
            }
            """);
        var log = new List<BuildLogEntry>();
        var request = project.Request();

        var report = await new BuildPipeline().RunAsync(request, log: new SynchronousProgress<BuildLogEntry>(log.Add),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.Success);
        var error = Assert.Single(report.Errors, e => e.File is not null);
        Assert.EndsWith("Jumper.cs", error.File, StringComparison.Ordinal);
        Assert.Equal(5, error.Line);
        Assert.Contains("CS0103", error.Message, StringComparison.Ordinal);
        Assert.Contains(log, e => e.Level == BuildLogLevel.Error && e.Step == "Compiling scripts");
        Assert.Null(report.OutputDirectory);
        Assert.False(Directory.Exists(Path.Combine(project.Output, $"Fixture Quest-{request.Target.RuntimeIdentifier}")));
    }

    [Fact]
    public async Task ScriptInfoDiagnosticsAreLoggedAsInfo()
    {
        using var project = TestProject.Create("""
            namespace Fixture;

            public sealed class Jumper : Script
            {
                public float Height = 120;

                protected override void Update() => Log.Debug("Height " + Height);
            }
            """);
        var log = new List<BuildLogEntry>();

        var report = await new BuildPipeline().RunAsync(project.Request(), log: new SynchronousProgress<BuildLogEntry>(log.Add),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors.Select(e => e.Message)));
        var info = Assert.Single(log, e => e.Message.StartsWith("TS1003", StringComparison.Ordinal));
        Assert.Equal(BuildLogLevel.Info, info.Level);
        Assert.EndsWith("Jumper.cs", info.File, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingStartSceneIsAnError()
    {
        using var project = TestProject.Create();
        project.WriteText("config/game.json", """{ "title": "Fixture", "startScene": "scenes/gone.tscene" }""");

        var report = await new BuildPipeline().RunAsync(project.Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(report.Success);
        Assert.Contains(report.Errors, e => e.Message.Contains("scenes/gone.tscene", StringComparison.Ordinal) && e.File == "config/game.json");
    }

    [Fact]
    public async Task TheLoadingScreenImageShipsInThePack()
    {
        using var project = TestProject.Create();
        project.WriteText("config/game.json", """{ "title": "Fixture", "startScene": "scenes/main.tscene", "loadingScreen": { "image": "sprites/unused.png" } }""");

        var report = await new BuildPipeline().RunAsync(project.Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var pack = new PackAssetSource(Path.Combine(report.OutputDirectory!, "game", ContentPack.FileName));
        Assert.Contains(pack.Entries, e => e.Path == "sprites/unused.png");
        Assert.DoesNotContain(report.Warnings, w => w.Message.Contains("loading screen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AMissingLoadingScreenImageIsAWarning()
    {
        using var project = TestProject.Create();
        project.WriteText("config/game.json", """{ "title": "Fixture", "startScene": "scenes/main.tscene", "loadingScreen": { "image": "ui/gone.png" } }""");

        var report = await new BuildPipeline().RunAsync(project.Request(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Contains(report.Warnings, w => w.Message.Contains("ui/gone.png", StringComparison.Ordinal) && w.File == "config/game.json");
    }

    [Fact]
    public async Task StepsReportProgressAndUnexpectedFailuresBecomeErrors()
    {
        using var project = TestProject.Create();
        var reported = new List<BuildProgress>();
        var pipeline = new BuildPipeline([new FixtureStep("Halfway", context => context.Progress(0.5, "half")), new FixtureStep("Broken", _ => throw new FormatException("bad"))]);

        var report = await pipeline.RunAsync(project.Request(), new SynchronousProgress<BuildProgress>(reported.Add), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(reported, p => p is { Title: "Halfway", Fraction: 0.5, Detail: "half" });
        Assert.False(report.Success);
        Assert.Contains(report.Errors, e => e.Message == "Broken failed unexpectedly: FormatException: bad");
    }

    [Fact]
    public void CodeReferencesFindLiteralsAndFolderPrefixes()
    {
        var file = Path.GetTempFileName();
        File.WriteAllText(file, """class A { void M(string n) { Load("audio/hit.wav"); Load($"cutscenes/{n}.cutscene"); var raw = "a"; } }""");
        try
        {
            var literals = CodeReferences.FromSources([file], TestContext.Current.CancellationToken).ToArray();
            Assert.Contains("audio/hit.wav", literals);
            Assert.Contains("cutscenes/", literals);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private sealed class FixtureStep(string title, Action<BuildContext> run) : IBuildStep
    {
        public string Title => title;

        public Task ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
        {
            run(context);
            return Task.CompletedTask;
        }
    }

    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Lock _lock = new();

        public void Report(T value)
        {
            lock (_lock)
                report(value);
        }
    }
}
