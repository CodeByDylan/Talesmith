using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Diagnostics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;
using Talesmith.Systems;
using Talesmith.Time;

namespace Talesmith.Scripting.Tests;

public sealed class LifecycleTests
{
    [Fact]
    public async Task SceneScriptsAreCreatedEnabledToldTheSceneLoadedAndStartedBeforeTheirFirstUpdate()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Recorder>(), SceneWith(Script<Recorder>()));
        var recorder = Single<Recorder>(test);
        Assert.Equal(["OnCreate", "OnEnable", "OnSceneLoaded"], recorder.Calls);

        test.Tick(3);

        Assert.Equal(["OnCreate", "OnEnable", "OnSceneLoaded", "OnStart"], recorder.Calls.Take(4));
        Assert.Equal(1, recorder.Count("OnStart"));
        Assert.Equal(3, recorder.Count("Update"));
        Assert.Equal(3, recorder.Count("LateUpdate"));
        Assert.True(recorder.Calls.IndexOf("FixedUpdate") < recorder.Calls.IndexOf("Update"));
        Assert.True(recorder.Calls.IndexOf("Update") < recorder.Calls.IndexOf("LateUpdate"));
    }

    [Fact]
    public async Task ScriptsAddedWhilePlayingAreCreatedAtTheNextPhaseAndStartedOnce()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var recorder = new Recorder();
        test.World.Create(new Transform(), new ScriptComponent(recorder));
        Assert.Empty(recorder.Calls);

        test.Tick(2);

        Assert.Equal(["OnCreate", "OnEnable", "OnStart"], recorder.Calls.Take(3));
        Assert.Equal(1, recorder.Count("OnStart"));
        Assert.Equal(0, recorder.Count("OnSceneLoaded"));
    }

    [Fact]
    public async Task TogglingEnabledAndInactiveCallsOnDisableAndOnEnable()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var recorder = new Recorder();
        var entity = test.World.Create(new Transform(), new ScriptComponent(recorder));
        test.Tick();

        recorder.Calls.Clear();
        recorder.Enabled = false;
        test.Tick();
        recorder.Enabled = true;
        Assert.Equal(["OnDisable", "OnEnable"], recorder.Calls);

        recorder.Calls.Clear();
        test.World.Set(entity, new Inactive());
        test.Tick();
        Assert.False(recorder.IsActiveAndEnabled);
        test.World.Remove<Inactive>(entity);
        Assert.Equal(["OnDisable", "OnEnable"], recorder.Calls);
        Assert.True(recorder.IsActiveAndEnabled);
    }

    [Fact]
    public async Task ScriptsDisabledBeforeTheirFirstUpdateStartWhenFirstEnabled()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var recorder = new Recorder { Enabled = false };
        test.World.Create(new Transform(), new ScriptComponent(recorder));
        test.Tick(2);
        Assert.Equal(["OnCreate"], recorder.Calls);

        recorder.Enabled = true;
        test.Tick();

        Assert.Equal(["OnCreate", "OnEnable", "OnStart"], recorder.Calls.Take(3));
    }

    [Fact]
    public async Task DestroyingTheEntityOrRemovingTheScriptCallsOnDisableAndOnDestroyOnce()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var first = new Recorder();
        var second = new Recorder();
        var entity = test.World.Create(new Transform(), new ScriptComponent(first, second));
        test.Tick();
        first.Calls.Clear();
        second.Calls.Clear();

        Assert.True(test.World.Get<ScriptComponent>(entity).Remove(second));
        test.World.Destroy(entity);
        test.Tick();

        Assert.Equal(["OnDisable", "OnDestroy"], first.Calls);
        Assert.Equal(["OnDisable", "OnDestroy"], second.Calls);
        Assert.True(first.IsDestroyed);
    }

    [Fact]
    public async Task UnloadingTheSceneTellsScriptsAndDestroysThem()
    {
        var test = await ScriptTestGame.StartAsync(s => s.AddScript<Recorder>(), SceneWith(Script<Recorder>()));
        await using var _ = test;
        var recorder = Single<Recorder>(test);
        test.Tick();
        recorder.Calls.Clear();

        await test.UnloadAsync();

        Assert.Equal(["OnSceneUnloaded", "OnDisable", "OnDestroy"], recorder.Calls);
    }

    [Fact]
    public async Task ScriptsDoNotRunWhileTheEditorAuthorsTheScene()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Recorder>(), SceneWith(Script<Recorder>()), ExecutionModes.Edit);
        var recorder = Single<Recorder>(test);

        test.Tick(5);

        Assert.Empty(recorder.Calls);
        Assert.False(recorder.IsActiveAndEnabled);
    }

    [Fact]
    public async Task ScriptsMoveTheirEntityWithFieldsFromTheScene()
    {
        var fields = new JsonObject { ["speed"] = 120, ["direction"] = new JsonArray(0, 1) };
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Mover>(), SceneWith(Script<Mover>(fields)));
        var mover = Single<Mover>(test);
        Assert.Equal(120, mover.Speed);

        test.Tick(60);

        Assert.Equal(0, mover.Position.X, 3);
        Assert.InRange(mover.Position.Y, 115, 121);
    }

    [Fact]
    public async Task AFailingScriptIsLoggedAndDisabledWithoutStoppingOthers()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var thrower = new Thrower();
        var mover = new Mover { Speed = 60 };
        test.World.Create(new Transform(), new ScriptComponent(thrower));
        test.World.Create(new Transform(), new ScriptComponent(mover));

        test.Tick(10);

        Assert.Equal(SystemScheduler.MaxConsecutiveFailures, thrower.Calls);
        Assert.False(thrower.IsActiveAndEnabled);
        Assert.InRange(mover.Position.X, 9, 11);
    }

    [Fact]
    public async Task WaitsResumeOnTheGameThreadAndStopWithTheScript()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var waiter = new Waiter();
        var abandoned = new Waiter();
        test.World.Create(new Transform(), new ScriptComponent(waiter));
        var doomed = test.World.Create(new Transform(), new ScriptComponent(abandoned));
        test.Tick();
        Assert.Equal(1, waiter.Stage);
        Assert.Equal(1, abandoned.Stage);

        test.World.Destroy(doomed);
        test.Tick(45);

        Assert.Equal(3, waiter.Stage);
        Assert.Equal(1, abandoned.Stage);
    }

    [Fact]
    public async Task ScriptsAddScriptsThatStartLikeOthers()
    {
        await using var test = await ScriptTestGame.StartAsync();
        var spawner = new Spawner();
        var entity = test.World.Create(new Transform(), new ScriptComponent(spawner));
        test.Tick();
        var mover = Assert.IsType<Mover>(spawner.Spawned);
        Assert.Same(mover, spawner.GetScript<Mover>());

        test.Tick(30);

        Assert.True(mover.IsActiveAndEnabled);
        Assert.True(test.World.Get<Transform>(entity).Position.X > 4);
    }

    [Fact]
    public async Task InstantiateCopiesComponentsAndScriptsWithTheirFields()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Mover>());
        var original = test.World.Create(new Transform(new Vector2(5, 0)), new ScriptComponent(new Mover { Speed = 0, Direction = Vector2.UnitY }));
        test.World.Set(original, new Name("Original"));
        test.Tick();
        var mover = test.World.Get<ScriptComponent>(original).Get<Mover>()!;

        var copy = mover.Instantiate(original, new Vector2(50, 0));
        test.Tick();

        var copied = test.World.Get<ScriptComponent>(copy).Get<Mover>()!;
        Assert.NotSame(mover, copied);
        Assert.Equal(Vector2.UnitY, copied.Direction);
        Assert.True(copied.IsActiveAndEnabled);
        Assert.Equal(new Vector2(50, 0), test.World.Get<Transform>(copy).Position);
        Assert.Equal("Original", copied.Name);
    }

    [Fact]
    public async Task EachScriptTypeIsTimedUnderItsOwnMarkerAndCountedAsScriptTime()
    {
        await using var test = await ScriptTestGame.StartAsync();
        test.World.Create(new Transform(), new ScriptComponent(new Mover()));
        test.Tick(3);

        var frame = test.Game.Profilers.Game.LastFrame!;
        Assert.True(frame.MarkerCallCount(ProfilerMarker.Get("Scripts/Mover", "Scripts")) > 0);
        Assert.True(frame.CounterValue(ProfilerCounter.Get("Script time", CounterKind.PerFrame, "ms")) > 0);
        Assert.Equal(1, frame.CounterValue(ProfilerCounter.Get("Scripts", CounterKind.Gauge, "scripts")));
    }

    [Fact]
    public async Task DispatchingUpdatesDoesNotAllocate()
    {
        await using var test = await ScriptTestGame.StartAsync();
        for (var i = 0; i < 200; i++)
            test.World.Create(new Transform(), new ScriptComponent(new Busy(), new Mover()));
        test.Tick(3);
        var runtime = test.Service<ScriptRuntime>();
        var profiler = test.Game.Profilers.Game;
        var time = new GameTime(1 / 60f, 1, 1 / 60f, 1, 1, 0);

        profiler.BeginFrame();
        runtime.Run(ScriptCallback.Update, time);
        // Steady-state allocations show in every round; a one-off, such as the runtime's own work, in only one.
        var fewest = long.MaxValue;
        for (var round = 0; round < 5 && fewest > 0; round++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
                runtime.Run(ScriptCallback.Update, time);
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        profiler.EndFrame();

        Assert.Equal(0, fewest);
    }

    internal static SceneDocument SceneWith(params ComponentDocument[] components)
    {
        var scene = SceneDocument.Create();
        scene.Entities.Add(new EntityDocument
        {
            Id = Guid.NewGuid(),
            Name = "Scripted",
            Components = [new ComponentDocument("Transform", []), .. components]
        });
        return scene;
    }

    internal static ComponentDocument Script<T>(JsonObject? fields = null) where T : Script =>
        new("ScriptComponent", new JsonObject
        {
            ["scripts"] = new JsonArray(new JsonObject { ["type"] = typeof(T).FullName, ["enabled"] = true, ["fields"] = fields ?? [] })
        });

    internal static T Single<T>(ScriptTestGame test) where T : Script
    {
        foreach (var archetype in test.World.Query<ScriptComponent>())
        {
            foreach (var component in archetype.GetSpan<ScriptComponent>())
            {
                if (component.Get<T>() is { } script)
                    return script;
            }
        }

        throw new InvalidOperationException($"No {typeof(T).Name} in the scene.");
    }
}
