using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Viewport;

namespace Talesmith.Editor.Tests;

/// <summary>An editor opened headless on a new platformer project; use it on the UI thread through <see cref="Headless"/>.</summary>
internal sealed class EditorFixture : IAsyncDisposable
{
    private readonly ServiceProvider _application;
    private readonly string _folder;

    private EditorFixture(ServiceProvider application, ServiceProvider services, string folder, EditorConsole console)
    {
        _application = application;
        Services = services;
        _folder = folder;
        Console = console;
    }

    public ServiceProvider Services { get; }

    public EditorConsole Console { get; }

    public SceneDocumentModel Document => Get<ISceneDocumentService>().Active!;

    public IEditWorld World => Get<IEditWorld>();

    /// <param name="prepare">Changes the new project's folder before the editor opens it.</param>
    /// <param name="beforeStart">Looks at the project's services before the project starts opening.</param>
    /// <param name="runFrames">Whether waiting for the project runs edit game frames, as the Scene panel does; without them the editor has to
    /// run what opening needs itself, as it does when another panel fills the window.</param>
    public static async Task<EditorFixture> OpenAsync(string template = "platformer", Action<string>? prepare = null, IGameSessionFactory? sessions = null,
        Action<ServiceProvider>? beforeStart = null, bool runFrames = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "talesmith-tests", Guid.NewGuid().ToString("N"));
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        var folder = await creator.CreateAsync(creator.Templates.Single(t => t.Id == template), root, "Fixture");
        prepare?.Invoke(folder);
        var application = new ServiceCollection()
            .AddTalesmithEditorApplication(new JsonSettingsService(null), sessions ?? new HeadlessGameSessionFactory())
            .BuildServiceProvider();
        var console = new EditorConsole();
        var services = await application.GetRequiredService<EditorHost>().BuildAsync(new EditorProject(folder), console);
        var fixture = new EditorFixture(application, services, root, console);
        beforeStart?.Invoke(services);
        services.GetRequiredService<ProjectService>().Start();
        _ = services.GetRequiredService<ISceneDocumentService>().OpenStartupSceneAsync();
        await fixture.WaitAsync(() => fixture.Get<IProjectService>().IsReady && fixture.Get<ISceneDocumentService>().Active is not null
                                       && fixture.World.World is not null && !fixture.World.IsBusy, runFrames: runFrames);
        return fixture;
    }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Runs edit game frames until the condition holds, or only waits for it when <paramref name="runFrames"/> is false.</summary>
    public async Task WaitAsync(Func<bool> condition, int timeoutMilliseconds = 30000, bool runFrames = true)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMilliseconds)
                throw new TimeoutException($"The editor did not reach the expected state. {Describe()} Console: " + string.Join(" | ", Console.Entries.Select(e => e.Message)));
            if (runFrames)
                Get<IProjectService>().EditSession?.Game.Tick(1.0 / 60);
            await Task.Delay(5);
        }
    }

    /// <summary>The state of the scene view and the scripts, for timeout messages.</summary>
    private string Describe()
    {
        var world = World;
        var shown = world.Game is null ? "no game" : ReferenceEquals(world.Game, Get<IProjectService>().EditSession?.Game) ? "the current edit game" : "an older edit game";
        var scripts = Services.GetService<IScriptService>() is { } service
            ? $"Scripts are {(service.IsCompiling ? "compiling" : "idle")}, last compiled {(service.LastResult is { Success: true } ? "successfully" : "with errors or not yet")}."
            : "";
        return $"The scene view shows {(world.World is null ? "no world" : "a world")} of {shown} and is {(world.IsBusy ? "busy" : "idle")}. {scripts}";
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        await _application.DisposeAsync();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
