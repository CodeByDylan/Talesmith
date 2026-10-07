using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Talesmith.Runtime.Components;

namespace Talesmith.Scripting.Compiler.Tests;

public sealed class HotReloadTests
{
    private const string WalkerV1 = """
        namespace TestGame;

        public sealed class Walker : Script
        {
            public float Speed = 10;

            public Entity Target;

            private int _updates;

            [Transient]
            public Follower? Partner;

            public int Updates => _updates;

            protected override void OnCreate() => Partner = GetScript<Follower>();

            protected override void Update()
            {
                _updates++;
                Position += new Vector2(Speed * Time.DeltaTime, 0);
            }
        }

        public sealed class Follower : Script
        {
            public string Label = "";
        }
        """;

    private const string WalkerV2 = """
        namespace TestGame;

        public sealed class Walker : Script
        {
            public float Speed = 10;

            public Entity Target;

            private int _updates;

            [Transient]
            public Follower? Partner;

            public int Enables;

            public int Updates => _updates;

            protected override void OnEnable() => Enables++;

            protected override void Update()
            {
                _updates++;
                Position += new Vector2(0, Speed * Time.DeltaTime);
            }
        }

        public sealed class Follower : Script
        {
            public string Label = "";

            public int Version = 2;
        }
        """;

    [Fact]
    public async Task ReloadingKeepsFieldValuesAndSwitchesToTheNewCode()
    {
        using var project = new TempProject();
        project.Write("Walker.cs", WalkerV1);
        using var compiler = new ScriptCompiler(project.Options);
        var (game, v1) = await StartAsync(compiler);
        await using var _ = game;
        var walker = game.Find("TestGame.Walker");
        var position = game.World.Get<Transform>(walker.Entity).Position;
        Assert.True(position.X > 0);

        project.Write("Walker.cs", WalkerV2);
        var v2 = (await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load();
        var result = game.Service<ScriptReloader>().Reload(v2);

        Assert.Equal(HotReloadStatus.Applied, result.Status);
        Assert.Equal(2, result.ReloadedScripts);
        var reloaded = game.Find("TestGame.Walker");
        Assert.NotSame(walker, reloaded);
        Assert.True(walker.IsDestroyed);
        Assert.Same(v2.Assembly, reloaded.GetType().Assembly);
        Assert.Same(v2, game.Service<ScriptTypeRegistry>().Assembly);
        dynamic state = reloaded;
        Assert.Equal(25f, (float)state.Speed);
        Assert.Equal(10, (int)state.Updates);
        Assert.Equal(1, (int)state.Enables);
        Assert.Same(game.Find("TestGame.Follower"), (object)state.Partner);
        Assert.Equal("partner", (string)((dynamic)game.Find("TestGame.Follower")).Label);

        game.Tick(10);

        Assert.Equal(20, (int)state.Updates);
        var moved = game.World.Get<Transform>(reloaded.Entity).Position;
        Assert.Equal(position.X, moved.X, 3);
        Assert.True(moved.Y > position.Y);
        Assert.NotSame(v1.Assembly, v2.Assembly);
    }

    [Fact]
    public async Task OldScriptsUnloadAfterAReload()
    {
        using var project = new TempProject();
        project.Write("Walker.cs", WalkerV1);
        using var compiler = new ScriptCompiler(project.Options);
        var (game, unloaded) = await StartAndReloadAsync(compiler, project);
        await using var _ = game;

        for (var i = 0; i < 20 && unloaded.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            game.Tick();
        }

        Assert.False(unloaded.IsAlive, "The first compilation's load context is still referenced.");
    }

    [Fact]
    public async Task RemovedScriptsOrIncompatibleFieldsRequireARestart()
    {
        using var project = new TempProject();
        project.Write("Walker.cs", WalkerV1);
        using var compiler = new ScriptCompiler(project.Options);
        var (game, _) = await StartAsync(compiler);
        await using var __ = game;
        var walker = game.Find("TestGame.Walker");

        project.Write("Walker.cs", WalkerV1.Replace("public float Speed = 10;", "public string Speed = \"fast\";").Replace(
            "Speed * Time.DeltaTime", "Time.DeltaTime").Replace("Follower", "Leader", StringComparison.Ordinal));
        var changed = (await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load();
        var result = game.Service<ScriptReloader>().Reload(changed);

        Assert.True(result.RestartRequired);
        Assert.Contains(result.Reasons, r => r.Contains("TestGame.Walker.speed", StringComparison.Ordinal) && r.Contains("String", StringComparison.Ordinal));
        Assert.Contains(result.Reasons, r => r.Contains("TestGame.Follower", StringComparison.Ordinal) && r.Contains("no longer exists", StringComparison.Ordinal));
        Assert.Same(walker, game.Find("TestGame.Walker"));
        Assert.False(walker.IsDestroyed);
        changed.Unload();
    }

    [Fact]
    public async Task ScriptsDeclaringSystemsRequireARestart()
    {
        using var project = new TempProject();
        project.Write("Walker.cs", WalkerV1);
        using var compiler = new ScriptCompiler(project.Options);
        var (game, _) = await StartAsync(compiler);
        await using var __ = game;

        project.Write("Spin.cs", "namespace TestGame; public sealed class SpinSystem : ISystem { public void Update(in SystemContext context) { } }");
        var changed = (await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load();
        var result = game.Service<ScriptReloader>().Check(changed);

        Assert.True(result.RestartRequired);
        Assert.Contains("SpinSystem", Assert.Single(result.Reasons));
    }

    [Fact]
    public async Task MissingScriptsAreRestoredWhenTheirTypeIsBack()
    {
        using var project = new TempProject();
        project.Write("Walker.cs", WalkerV1);
        using var compiler = new ScriptCompiler(project.Options);
        var scene = TestGame.SceneWith(("TestGame.Walker", []), ("TestGame.Jumper", new JsonObject { ["height"] = 3 }));
        await using var game = await TestGame.StartAsync((await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load(), scene);
        game.Tick();
        Assert.Contains("TestGame.Jumper", game.Service<ScriptTypeRegistry>().MissingTypes);

        project.Write("Jumper.cs", "namespace TestGame; public sealed class Jumper : Script { public float Height; public bool Started; protected override void OnStart() => Started = true; }");
        var result = game.Service<ScriptReloader>().Reload((await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load());
        game.Tick();

        Assert.Equal((HotReloadStatus.Applied, 1), (result.Status, result.RestoredScripts));
        dynamic jumper = game.Find("TestGame.Jumper");
        Assert.Equal(3f, (float)jumper.Height);
        Assert.True((bool)jumper.Started);
        Assert.Empty(game.Service<ScriptTypeRegistry>().MissingTypes);
    }

    private static async Task<(TestGame Game, ScriptAssembly Scripts)> StartAsync(ScriptCompiler compiler)
    {
        var result = await compiler.CompileAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var scripts = result.Load();
        var scene = TestGame.SceneWith(("TestGame.Walker", new JsonObject { ["speed"] = 25 }), ("TestGame.Follower", new JsonObject { ["label"] = "partner" }));
        var game = await TestGame.StartAsync(scripts, scene);
        game.Tick(10);
        return (game, scripts);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(TestGame Game, WeakReference Unloaded)> StartAndReloadAsync(ScriptCompiler compiler, TempProject project)
    {
        var (game, v1) = await StartAsync(compiler);
        project.Write("Walker.cs", WalkerV2);
        var v2 = (await compiler.CompileAsync(TestContext.Current.CancellationToken)).Load();
        Assert.Equal(HotReloadStatus.Applied, game.Service<ScriptReloader>().Reload(v2).Status);
        game.Tick();
        return (game, v1.Unload());
    }
}
