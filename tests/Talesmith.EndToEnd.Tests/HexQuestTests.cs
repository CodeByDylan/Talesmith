using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Input;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Rendering;

namespace Talesmith.EndToEnd.Tests;

/// <summary>Plays the Hex Quest sample with its plugins, driving the hero with the mouse.</summary>
public sealed class HexQuestTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(2560, 1080)]
    [InlineData(800, 1000)]
    public async Task ClickingTheHarborWalksTheHeroIntoItsTriggerAndStartsItsCutscene(int width, int height)
    {
        var assets = Path.Combine(_workspace.CopySample("HexQuest"), "assets");
        Assert.SkipUnless(File.Exists(Path.Combine(assets, "plugins", "cutscenes", "Talesmith.Samples.Cutscenes.dll")), "The sample plugins are not built.");
        using var loggers = new RecordingLoggers();
        await using var game = await HeadlessGame.StartAsync(assets, loggers.Factory);
        game.Game.Viewport.Size = new Vector2(width, height);
        game.Run(30);
        var hero = game.Find("Hero");
        var harbor = game.Find("Harbor");
        Assert.False(hero.IsNull);
        Assert.True(game.World.Has<TriggerArea>(harbor));
        var control = game.Game.Services.GetRequiredService<PlayerControl>();
        var target = game.World.Get<Transform>(harbor).Position;
        var click = game.Game.Services.GetRequiredService<RenderContext>().WorldToScreen(target);

        game.Input.MouseMove(click);
        game.Input.MouseDown(MouseButton.Left, click);
        game.Run(1);
        game.Input.MouseUp(MouseButton.Left, click);
        var clock = Stopwatch.StartNew();
        while (!control.Reasons.Contains("cutscene"))
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"The harbor-arrival cutscene did not start; the hero is at {Position(game, hero)}, the harbor at {target}.");
            game.Run(1);
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }

        Assert.Equal(harbor, game.World.Get<TriggerActivator>(hero).Inside);
        Assert.Empty(loggers.Problems);
    }

    [Fact]
    public async Task ClicksOnTheBarsBesideAFitViewDoNotMoveTheHero()
    {
        var assets = Path.Combine(_workspace.CopySample("HexQuest"), "assets");
        Assert.SkipUnless(File.Exists(Path.Combine(assets, "plugins", "hexquest", "Talesmith.Samples.HexQuest.dll")), "The sample plugins are not built.");
        var config = Path.Combine(assets, "config", "game.json");
        var settings = JsonNode.Parse(await File.ReadAllTextAsync(config, TestContext.Current.CancellationToken), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip
        })!;
        settings["view"]!["scaleMode"] = "fit";
        await File.WriteAllTextAsync(config, settings.ToJsonString(), TestContext.Current.CancellationToken);
        using var loggers = new RecordingLoggers();
        await using var game = await HeadlessGame.StartAsync(assets, loggers.Factory);
        game.Game.Viewport.Size = new Vector2(2560, 1080);
        game.Run(30);
        var hero = game.Find("Hero");
        var start = Position(game, hero);
        var bar = new Vector2(100, 540);
        Assert.False(game.Game.Viewport.Layout.ViewRect.Contains(bar));

        game.Input.MouseMove(bar);
        game.Input.MouseDown(MouseButton.Left, bar);
        game.Run(1);
        game.Input.MouseUp(MouseButton.Left, bar);
        game.Run(120);

        Assert.Equal(start, Position(game, hero));
        Assert.Empty(loggers.Problems);
    }

    private static Vector2 Position(HeadlessGame game, Entity entity) => game.World.Get<Transform>(entity).Position;
}
