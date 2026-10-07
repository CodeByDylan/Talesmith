using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Console;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Viewport;
using Talesmith.Screenshots.Capture;
using Talesmith.Scripting;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>Creates template projects in a temporary folder and opens them in a headless editor.</summary>
internal static class EditorFixture
{
    private static readonly Lazy<string> ProjectsFolder = new(CreateProjects);

    public static string Platformer => Path.Combine(ProjectsFolder.Value, "Coral Cove");

    public static string HexAdventure => Path.Combine(ProjectsFolder.Value, "Ember Isles");

    /// <summary>A copy of the Isle Hopper sample, so opening it does not add .meta files to the repository.</summary>
    public static string IsleHopper => Path.Combine(ProjectsFolder.Value, "IsleHopper");

    /// <summary>A copy of the Hex Quest sample.</summary>
    public static string HexQuest => Path.Combine(ProjectsFolder.Value, "HexQuest");

    /// <summary>A folder for files a scene needs, such as an image to import.</summary>
    public static string Scratch => Directory.CreateDirectory(Path.Combine(ProjectsFolder.Value, "scratch")).FullName;

    /// <summary>A platformer whose scripts have a compile error.</summary>
    public static string BrokenScripts => Path.Combine(ProjectsFolder.Value, "Tide Runner");

    public static ServiceProvider CreateApplication(ISettingsService? settings = null) =>
        new ServiceCollection()
            .AddTalesmithEditorApplication(settings ?? new JsonSettingsService(null), new HeadlessGameSessionFactory())
            .BuildServiceProvider();

    /// <summary>Disposes an editor's or an application's services and waits until they have stopped.</summary>
    public static void Dispose(ServiceProvider services)
    {
        var disposal = services.DisposeAsync().AsTask();
        RenderLoop.Wait(() => disposal.IsCompleted, 30000);
    }

    /// <summary>Opens a project in an editor whose window content is <see cref="OpenEditor.Root"/>; call <see cref="OpenEditor.Attach"/> once it is shown.</summary>
    /// <remarks>The editor starts from the default layout and editor state, such as the viewport cameras, whatever a screenshot taken before changed in the
    /// same project.</remarks>
    public static OpenEditor Open(string folder)
    {
        var project = new EditorProject(folder);
        string[] state = [project.GetStatePath(LayoutService.FileName), project.GetStatePath(ProjectState.FileName)];
        foreach (var path in state.Where(File.Exists))
            File.Delete(path);
        var application = CreateApplication();
        var host = application.GetRequiredService<EditorHost>();
        var console = new EditorConsole();
        var build = host.BuildAsync(project, console);
        RenderLoop.Wait(() => build.IsCompleted, 30000);
        var provider = build.Result;
        provider.GetRequiredService<ProjectService>().Start();
        _ = provider.GetRequiredService<ISceneDocumentService>().OpenStartupSceneAsync();
        var shell = provider.GetRequiredService<ShellViewModel>();
        var toasts = new ToastHost();
        var dialogs = new DialogHost { Content = new Panel { Children = { new ShellView { DataContext = shell }, toasts } } };
        dialogs.DataTemplates.AddRange(DialogTemplates.Load());
        return new OpenEditor(application, provider, shell, dialogs, toasts, console);
    }

    private static string CreateProjects()
    {
        var folder = Path.Combine(Path.GetTempPath(), "talesmith-screenshots", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(folder);
        var creator = new ProjectCreator([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);
        Task.Run(async () =>
        {
            await creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), folder, "Coral Cove");
            await creator.CreateAsync(creator.Templates.Single(t => t.Id == "hex-adventure"), folder, "Ember Isles");
            await creator.CreateAsync(creator.Templates.Single(t => t.Id == "platformer"), folder, "Tide Runner");
        }).GetAwaiter().GetResult();
        CopyFolder(Path.Combine(Repository.Root, "samples", "IsleHopper"), Path.Combine(folder, "IsleHopper"));
        CopyFolder(Path.Combine(Repository.Root, "samples", "HexQuest"), Path.Combine(folder, "HexQuest"));
        WriteScripts(Path.Combine(folder, "Tide Runner", "assets", "scripts"));
        return folder;
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void WriteScripts(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Coin.cs"), """
            namespace TideRunner;

            /// <summary>Spins in place until the player picks it up.</summary>
            public sealed class Coin : Script
            {
                [Range(0, 10)]
                public float SpinSpeed = 3;

                protected override void Update() => Transform.Rotation += SpinSpeed * Time.DeltaTime;
            }
            """);
        File.WriteAllText(Path.Combine(folder, "PlayerDash.cs"), """
            namespace TideRunner;

            /// <summary>Dashes forward when the Dash action is pressed.</summary>
            public sealed class PlayerDash : Script
            {
                [Range(0, 2000)]
                public float DashSpeed = 900;

                public float Cooldown = 0.6f;

                private float _ready;

                protected override void Update()
                {
                    _ready -= Time.DeltaTime;
                    if (Input.WasPressed("Dash") && _ready <= 0)
                    {
                        Position += new Vector2(dashSpeed * Time.DeltaTime, 0);
                        _ready = Cooldown;
                    }
                }
            }
            """);
    }

    /// <summary>Copies a project, leaving out the editor state of whoever opened it last.</summary>
    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            if (Path.GetFileName(directory) != ".talesmith")
                CopyFolder(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}

/// <summary>An editor opened by <see cref="EditorFixture"/>, with the application services it was built from.</summary>
internal sealed record OpenEditor(ServiceProvider Application, ServiceProvider Services, ShellViewModel Shell, DialogHost Root, ToastHost Toasts, IConsole Console)
{
    /// <summary>Disposes the project's services, then the application's, so nothing of the editor stays alive.</summary>
    public void Close()
    {
        EditorFixture.Dispose(Services);
        EditorFixture.Dispose(Application);
    }

    /// <summary>Connects dialogs and toasts to the window and waits until the scene shows in the viewport with the project's scripts.</summary>
    public void Attach(Window window)
    {
        Services.GetRequiredService<WindowHost>().Attach(window, Root, Toasts);
        var project = Services.GetRequiredService<IProjectService>();
        var world = Services.GetRequiredService<IEditWorld>();
        var documents = Services.GetRequiredService<ISceneDocumentService>();
        try
        {
            RenderLoop.Wait(() =>
            {
                if (project.WhenReady.Exception is { } failed)
                    throw new InvalidOperationException($"{project.Project.Folder} did not open.", failed.InnerException);
                return project.IsReady && documents.Active is not null && world.World is not null && !world.IsBusy && world.EntityCount > 0;
            }, 60000);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{project.Project.Folder} did not open: ready {project.IsReady}, scene {documents.Active is not null}, world {world.World is not null}, " +
                                       $"busy {world.IsBusy}, {world.EntityCount} entities.", ex);
        }

        WaitForScripts(project, world);
        RenderLoop.Wait(() => Root.GetVisualDescendants().OfType<ProjectLoadingView>().All(view => !view.IsVisible), 10000);
        RenderLoop.Settle(300);
    }

    /// <summary>Waits until the first compilation finished and the scene view shows the scene in an edit game that has its scripts.</summary>
    private void WaitForScripts(IProjectService project, IEditWorld world)
    {
        var scripts = Services.GetRequiredService<IScriptService>();
        RenderLoop.Wait(() =>
        {
            if (scripts.LastResult is null || scripts.IsCompiling)
                return false;
            if (scripts.Assembly is not { } assembly)
                return true;
            var game = project.EditSession?.Game;
            return game is not null && ReferenceEquals(game.Services.GetService<ScriptTypeRegistry>()?.Assembly, assembly) && ReferenceEquals(world.Game, game)
                   && world.World is not null && !world.IsBusy;
        }, 120000);
    }

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();
}

/// <summary>Waits for play mode by asking its game thread.</summary>
internal static class PlayWait
{
    /// <summary>Pumps until play mode runs a loaded scene.</summary>
    public static void UntilSceneLoaded(IPlayModeService play)
    {
        Task<bool>? probe = null;
        var loaded = false;
        RenderLoop.Wait(() =>
        {
            if (probe is { IsCompleted: true })
            {
                loaded = probe.Result;
                probe = null;
            }

            if (!loaded && play.State == PlayState.Playing)
                probe ??= play.TryInvokeAsync(game => game.Scenes.Current is not null && !game.Scenes.IsLoading, false);
            return loaded;
        }, 60000);
    }
}
