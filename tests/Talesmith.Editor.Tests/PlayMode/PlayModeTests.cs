using System.Diagnostics;
using System.Numerics;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Shell;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Docking;

namespace Talesmith.Editor.Tests.PlayMode;

public sealed class PlayModeTests
{
    [Fact]
    public void PlayingAndStoppingLeavesTheDocumentSelectionAndEditWorldUntouched() => Headless.Run(async () =>
    {
        var sessions = new ObservedSessionFactory();
        await using var editor = await EditorFixture.OpenAsync(sessions: sessions);
        var model = editor.Document;
        var hero = model.Entities.Single(e => e.Name == "Hero");
        model.Rename(hero.Id, "Unsaved hero");
        editor.Get<ISelectionService>().SelectEntity(hero.Id);
        var before = DocumentSerializer.Write(model.Document);
        var editEntities = editor.World.EntityCount;
        var play = editor.Get<IPlayModeService>();
        var entered = 0;
        sessions.Created += session => session.Game.Services.GetRequiredService<IEventBus>().Subscribe((ref PlayModeEntered _) => Interlocked.Increment(ref entered));

        await play.PlayAsync();
        Assert.Equal(PlayState.Playing, play.State);
        var game = play.Game!;
        Assert.Equal(GameThreading.Dedicated, game.Threading);
        Assert.False(game.IsGameThread);
        await WaitUntilAsync(() => play.InvokeAsync(g => g.Scenes.Current is DocumentScene && !g.Scenes.IsLoading));

        var renamed = await play.InvokeAsync(g =>
        {
            var scene = (DocumentScene)g.Scenes.Current!;
            scene.World.Get<Transform>(scene.Entities!.Entities[hero.Id]).Position = new Vector2(9999, 9999);
            return scene.Document!.Entities.Any(e => e.Name == "Unsaved hero");
        });
        Assert.True(renamed);
        Assert.Equal(1, Volatile.Read(ref entered));

        play.Pause();
        Assert.True(await play.InvokeAsync(g => g.IsPaused));
        var frame = await play.InvokeAsync(g => g.FrameCount);
        play.Step();
        await WaitUntilAsync(async () => await play.InvokeAsync(g => g.FrameCount) > frame + 2);
        Assert.True(await play.InvokeAsync(g => g.IsPaused));

        await play.StopAsync();
        Assert.Equal(PlayState.Stopped, play.State);
        Assert.Null(play.Game);
        Assert.Equal(GameThreading.Host, game.Threading);
        await Assert.ThrowsAsync<InvalidOperationException>(() => play.InvokeAsync(g => g.FrameCount));
        Assert.Equal(before, DocumentSerializer.Write(model.Document));
        Assert.Equal([hero.Id], editor.Get<ISelectionService>().Entities);
        Assert.Equal(editEntities, editor.World.EntityCount);
        Assert.True(editor.World.TryGetEntity(hero.Id, out var editHero));
        Assert.NotEqual(new Vector2(9999, 9999), editor.World.World!.Get<Transform>(editHero).Position);
    });

    [Fact]
    public void PlayingSwitchesToTheGamePanelWithoutRebuildingTheWorkspace() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var window = editor.Get<EditorWindow>();
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var dock = window.GetVisualDescendants().OfType<DockHost>().Single();
            var scene = dock.GetContent(PanelIds.Scene)!;
            var game = dock.GetContent(PanelIds.Game)!;
            var detached = 0;
            scene.DetachedFromVisualTree += (_, _) => detached++;
            dock.GetContent(PanelIds.Hierarchy)!.DetachedFromVisualTree += (_, _) => detached++;
            var play = editor.Get<IPlayModeService>();

            await play.PlayAsync();
            window.UpdateLayout();
            Assert.True(game.IsEffectivelyVisible);
            Assert.False(scene.IsEffectivelyVisible);

            await play.StopAsync();
            window.UpdateLayout();
            Assert.True(scene.IsEffectivelyVisible);
            Assert.Equal(0, detached);
        }
        finally
        {
            window.CloseApproved = true;
            window.Close();
        }
    });

    [Fact]
    public void DispatchedFailuresAreReportedToTheConsole() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var play = editor.Get<IPlayModeService>();
        await play.PlayAsync();

        play.Dispatch(_ => throw new InvalidOperationException("Broken on purpose"));
        await WaitUntilAsync(() => Task.FromResult(editor.Console.Entries.Any(e => e.Message.Contains("Broken on purpose", StringComparison.Ordinal))));
        Assert.Equal(PlayState.Playing, play.State);
        await play.StopAsync();
    });

    [Fact]
    public void AGameThatQuitsStopsPlayMode() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var play = editor.Get<IPlayModeService>();
        await play.PlayAsync();
        Assert.Equal(PlayState.Playing, play.State);

        play.Dispatch(game => game.Services.GetRequiredService<GameLifetime>().Quit());

        await WaitUntilAsync(() => Task.FromResult(play.State == PlayState.Stopped));
        Assert.Null(play.Session);
    });

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The play session did not reach the expected state.");
            await Task.Delay(10);
        }
    }

    private sealed class ObservedSessionFactory : IGameSessionFactory
    {
        private readonly HeadlessGameSessionFactory _inner = new();

        public event Action<GameSession>? Created;

        public async Task<GameSession> CreateAsync(DesktopGameOptions options, ILoggerFactory loggers, CancellationToken cancellationToken)
        {
            var session = await _inner.CreateAsync(options, loggers, cancellationToken);
            Created?.Invoke(session);
            return session;
        }
    }
}
