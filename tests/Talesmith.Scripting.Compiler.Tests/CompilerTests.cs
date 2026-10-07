using System.Text.Json.Nodes;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Scripting.Compiler.Tests;

public sealed class CompilerTests
{
    private const string Mover = """
        namespace TestGame;

        public sealed class Mover : Script
        {
            [Range(0, 500)]
            public float Speed = 10;

            protected override void Update() => Position += new Vector2(Speed * Time.DeltaTime, 0);
        }
        """;

    [Fact]
    public async Task CompiledScriptsLoadAndMoveTheirEntity()
    {
        using var project = new TempProject();
        project.Write("Mover.cs", Mover);
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.NotNull(result.Symbols);
        var scripts = result.Load();
        Assert.True(scripts.IsCollectible);
        Assert.Equal(["TestGame.Mover"], scripts.ScriptTypes.Select(t => t.FullName));
        await using var game = await TestGame.StartAsync(scripts, TestGame.SceneWith(("TestGame.Mover", new JsonObject { ["speed"] = 120 })));
        game.Tick(60);

        var mover = game.Find("TestGame.Mover");
        Assert.InRange(game.World.Get<Transform>(mover.Entity).Position.X, 115, 121);
        var properties = game.Service<ScriptTypeRegistry>().Describe(mover.GetType());
        Assert.Equal((PropertyKind.Number, 500d), (properties[0].Kind, properties[0].Max));
    }

    [Fact]
    public async Task ScriptFieldsCanHoldTexturesWithoutAUsingDirective()
    {
        using var project = new TempProject();
        project.Write("Banner.cs", "namespace TestGame; public sealed class Banner : Script { public TextureAsset? Image; }");
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    [Fact]
    public async Task ScriptsSetTheTimeScaleAndPause()
    {
        using var project = new TempProject();
        project.Write("SlowMotion.cs", """
            namespace TestGame;

            public sealed class SlowMotion : Script
            {
                protected override void Update()
                {
                    Time.TimeScale = 0.25f;
                    if (Time.FrameCount == 3)
                        Time.IsPaused = true;
                }
            }
            """);
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        await using var game = await TestGame.StartAsync(result.Load(), TestGame.SceneWith(("TestGame.SlowMotion", new JsonObject())));
        game.Tick(5);
        Assert.Equal(0.25f, game.Game.TimeScale);
        Assert.True(game.Game.IsPaused);
    }

    [Fact]
    public async Task ErrorsAreReportedWithTheirFileLineAndColumn()
    {
        using var project = new TempProject();
        project.Write("Good.cs", Mover);
        var broken = project.Write("Enemies/Broken.cs", """
            namespace TestGame.Enemies;

            public sealed class Broken : Script
            {
                protected override void Update() => Missing();
            }
            """);
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.Image);
        var error = Assert.Single(result.Errors);
        Assert.Equal(("CS0103", broken, "assets/scripts/Enemies/Broken.cs", 5, 41), (error.Id, error.FilePath, error.RelativePath, error.Line, error.Column));
        Assert.Contains("Missing", error.Message);
        Assert.StartsWith("assets/scripts/Enemies/Broken.cs(5,41): error CS0103:", error.ToString());
    }

    [Fact]
    public async Task UnchangedFilesKeepTheirSyntaxTrees()
    {
        using var project = new TempProject();
        project.Write("Mover.cs", Mover);
        project.Write("Spinner.cs", "namespace TestGame; public sealed class Spinner : Script { public float Speed = 1; }");
        project.Write("Tagger.cs", "namespace TestGame; public sealed class Tagger : Script { public string Tag = \"\"; }");
        using var compiler = new ScriptCompiler(project.Options);
        var first = await compiler.CompileAsync(TestContext.Current.CancellationToken);
        Assert.Equal((3, 0, 3), (first.SourceFiles, first.ReusedTrees, first.ParsedTrees));

        project.Write("Spinner.cs", "namespace TestGame; public sealed class Spinner : Script { public float Speed = 2; }");
        var second = await compiler.CompileAsync(TestContext.Current.CancellationToken);
        var third = await compiler.CompileAsync(TestContext.Current.CancellationToken);
        project.Delete("Tagger.cs");
        project.Write("Extra/Jumper.cs", "namespace TestGame.Extra; public sealed class Jumper : Script { }");
        var fourth = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.Equal((3, 2, 1), (second.SourceFiles, second.ReusedTrees, second.ParsedTrees));
        Assert.Equal((3, 3, 0), (third.SourceFiles, third.ReusedTrees, third.ParsedTrees));
        Assert.Equal((3, 2, 1), (fourth.SourceFiles, fourth.ReusedTrees, fourth.ParsedTrees));
        Assert.True(fourth.Success, string.Join("\n", fourth.Diagnostics));
        Assert.Equal(["TestGame.Extra.Jumper", "TestGame.Mover", "TestGame.Spinner"], fourth.Load().ScriptTypes.Select(t => t.FullName).Order());
        Assert.Equal(4, fourth.Generation);
    }

    [Fact]
    public async Task BinObjAndHiddenFoldersAreNotCompiled()
    {
        using var project = new TempProject();
        project.Write("Mover.cs", Mover);
        project.Write("bin/Generated.cs", "this does not compile");
        project.Write("obj/Generated.cs", "this does not compile");
        project.Write(".backup/Old.cs", "this does not compile");
        using var compiler = new ScriptCompiler(project.Options);

        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Equal(1, result.SourceFiles);
    }

    [Fact]
    public async Task DebugBuildsEmbedSourcesSoStackTracesShowScriptLines()
    {
        using var project = new TempProject();
        var path = project.Write("Thrower.cs", """
            namespace TestGame;

            public sealed class Thrower : Script
            {
                public void Explode()
                {
                    throw new InvalidOperationException("Boom");
                }
            }
            """);
        using var compiler = new ScriptCompiler(project.Options);
        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);
        var scripts = result.Load();
        var thrower = Activator.CreateInstance(scripts.ScriptTypes[0])!;

        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => thrower.GetType().GetMethod("Explode")!.Invoke(thrower, null));

        Assert.Contains($"{path}:line 7", error.InnerException!.StackTrace);
        using var symbols = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(result.Symbols!));
        var reader = symbols.GetMetadataReader();
        Assert.Contains(reader.CustomDebugInformation, handle => reader.GetGuid(reader.GetCustomDebugInformation(handle).Kind) ==
            new Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE"));
    }

    [Fact]
    public async Task ReleaseBuildsAreOptimizedAndCanBeWrittenForAGame()
    {
        using var project = new TempProject(ScriptConfiguration.Release);
        project.Write("Mover.cs", Mover);
        using var compiler = new ScriptCompiler(project.Options);
        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        var path = result.WriteTo(Path.Combine(project.Directory, "assets", "scripts", "bin"));

        Assert.Equal(Path.GetFullPath(Path.Combine(project.Directory, "assets", ScriptingOptions.DefaultAssemblyPath)), path);
        Assert.True(File.Exists(Path.ChangeExtension(path, ".pdb")));
        var loaded = ScriptAssembly.LoadFile(path);
        var debuggable = loaded.Assembly.GetCustomAttributes(typeof(System.Diagnostics.DebuggableAttribute), false).Cast<System.Diagnostics.DebuggableAttribute>();
        Assert.DoesNotContain(debuggable, d => d.IsJITOptimizerDisabled);
    }

    [Fact]
    public async Task ScriptsCanDeclareSystemsAndComponentsThatTheGameRegisters()
    {
        using var project = new TempProject();
        project.Write("Health.cs", """
            namespace TestGame;

            [Component(Category = "Gameplay")]
            public struct Regeneration
            {
                public float PerSecond;
                public float Amount;
            }

            [UpdateIn(SystemPhase.Update)]
            public sealed class RegenerationSystem : ISystem
            {
                public void Update(in SystemContext context)
                {
                    foreach (var archetype in context.World.Query<Regeneration>())
                    {
                        foreach (ref var regeneration in archetype.GetSpan<Regeneration>())
                            regeneration.Amount += regeneration.PerSecond * context.Time.DeltaTime;
                    }
                }
            }
            """);
        using var compiler = new ScriptCompiler(project.Options);
        var scripts = (await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load();
        Assert.True(scripts.DeclaresEngineTypes);
        var scene = SceneDocument.Create();
        scene.Entities.Add(new EntityDocument
        {
            Id = Guid.NewGuid(),
            Components = [new ComponentDocument("TestGame.Regeneration", new JsonObject { ["perSecond"] = 6 })]
        });
        await using var game = await TestGame.StartAsync(scripts, scene);

        game.Tick(30);

        var registry = game.Service<ComponentRegistry>();
        var definition = registry.Find("TestGame.Regeneration")!;
        var entity = game.World.Query<SceneEntityId>().GetEnumerator();
        Assert.True(entity.MoveNext());
        var data = definition.Capture(game.World, entity.Current.Entities[0], new NoContext())!;
        Assert.InRange(data["amount"]!.GetValue<float>(), 2.9f, 3.1f);
    }

    [Fact]
    public async Task TheWarmUpCompilesWithoutTouchingTheProject()
    {
        using var project = new TempProject();
        using var compiler = new ScriptCompiler(project.Options);

        await compiler.WarmUpAsync(TestContext.Current.CancellationToken);
        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(0, result.SourceFiles);
        Assert.Empty(result.Diagnostics);
        Assert.False(Directory.Exists(Path.Combine(project.Directory, "bin")));
    }

    private sealed class NoContext : ICaptureContext
    {
        public Assets.AssetGuid GetGuid(object asset) => default;

        public Guid GetEntityId(Ecs.Entity entity) => Guid.Empty;
    }
}
