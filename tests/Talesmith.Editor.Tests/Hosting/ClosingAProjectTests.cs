using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Viewport;

namespace Talesmith.Editor.Tests.Hosting;

/// <summary>
/// The application's services outlive every project, so a project-scoped object that one of them still reaches, through an event handler,
/// a timer or a cache, keeps the closed project's whole editor in memory until the editor exits.
/// </summary>
public sealed class ClosingAProjectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingAProjectReleasesItsEditorWithEveryPanelItShowed(bool playing) => Headless.Run(async () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "talesmith-tests", Guid.NewGuid().ToString("N"));
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        var folder = await creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), root, "Fixture");
        await using var application = new ServiceCollection()
            .AddTalesmithEditorApplication(new JsonSettingsService(null), new HeadlessGameSessionFactory())
            .BuildServiceProvider();
        var host = application.GetRequiredService<EditorHost>();
        try
        {
            var closed = await OpenAndCloseAsync(host, folder, playing);

            var alive = await CollectAsync(closed);

            Assert.True(alive.Count == 0, $"Still in memory after the project closed: {string.Join(", ", alive)}.");
        }
        finally
        {
            host.CloseHub();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    });

    /// <summary>
    /// Opens the project in its editor window, shows every panel, optionally plays the game, and closes the project, then returns what nothing
    /// may keep alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<IReadOnlyList<(string Name, WeakReference Reference)>> OpenAndCloseAsync(EditorHost host, string folder, bool playing)
    {
        Assert.True(await host.OpenProjectAsync(folder));
        var services = host.ProjectServices!;
        var project = services.GetRequiredService<ProjectService>();
        var documents = services.GetRequiredService<SceneDocumentService>();
        var world = services.GetRequiredService<IEditWorld>();
        var clock = Stopwatch.StartNew();
        while (!(project.IsReady && documents.Active is not null && world.World is not null && !world.IsBusy))
        {
            Assert.True(clock.ElapsedMilliseconds < 30000, "The project did not open.");
            project.EditSession?.Game.Tick(1.0 / 60);
            await Task.Delay(5);
        }

        var layout = services.GetRequiredService<LayoutService>();
        foreach (var panel in services.GetRequiredService<PanelRegistry>().Panels)
        {
            layout.ShowPanel(panel.Id);
            await Task.Delay(20);
        }

        var play = services.GetRequiredService<IPlayModeService>();
        if (playing)
        {
            await play.PlayAsync();
            Assert.True(play.IsPlaying);
        }

        await Task.Delay(1500);

        List<(string, WeakReference)> references =
        [
            ("the editor window", new WeakReference(host.EditorWindow)),
            (nameof(ShellViewModel), new WeakReference(services.GetRequiredService<ShellViewModel>())),
            (nameof(SceneDocumentService), new WeakReference(documents)),
            (nameof(EditorCommandRegistry), new WeakReference(services.GetRequiredService<EditorCommandRegistry>())),
            (nameof(ViewportOptions), new WeakReference(services.GetRequiredService<ViewportOptions>())),
            (nameof(StatusBarViewModel), new WeakReference(services.GetRequiredService<StatusBarViewModel>())),
            (nameof(ProjectService), new WeakReference(project)),
            ("the edit world", new WeakReference(world)),
            ("the project's services", new WeakReference(services)),
        ];
        if (playing)
            references.Add(("the played game", new WeakReference(play.Game)));
        Assert.True(await host.CloseProjectAsync());
        return references;
    }

    /// <summary>Collects garbage until every reference is gone, giving work the project queued before it closed a few seconds to finish.</summary>
    private static async Task<List<string>> CollectAsync(IReadOnlyList<(string Name, WeakReference Reference)> references)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var alive = references.Where(r => r.Reference.IsAlive).Select(r => r.Name).ToList();
            if (alive.Count == 0 || clock.ElapsedMilliseconds > 10000)
                return alive;
            await Task.Delay(50);
        }
    }
}
