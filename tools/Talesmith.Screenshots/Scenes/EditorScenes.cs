using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.CommandPalette;
using Talesmith.Editor.Console;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hub;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Viewport;
using Talesmith.Screenshots.Capture;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The editor with a template project open: the scene in the viewport, an entity selected and messages in the console.</summary>
internal class EditorWindowScene : ScreenshotScene
{
    private OpenEditor? _editor;

    public override string Name => "editor-window";

    public override Size Size => new(1440, 880);

    protected virtual string Project => EditorFixture.Platformer;

    protected OpenEditor Editor => _editor!;

    public override Control Build()
    {
        _editor = EditorFixture.Open(Project);
        return _editor.Root;
    }

    public override void Prepare(Window window)
    {
        Editor.Attach(window);
        var documents = Editor.Get<ISceneDocumentService>();
        if (documents.Active?.Entities.FirstOrDefault(e => e.Name == "Hero") is { } hero)
            Editor.Get<ISelectionService>().SelectEntity(hero.Id);
        Editor.Console.Warning("hero.png: the sprite 'hero-4' is not in the texture", ConsoleSource.Assets);
        RenderLoop.Settle(200);
        Customize(window);
    }

    public override void Release()
    {
        _editor?.Close();
        _editor = null;
    }

    protected virtual void Customize(Window window)
    {
    }
}

/// <summary>The top-down hex template, to show a hex tile map in the viewport.</summary>
internal sealed class EditorHexScene : EditorWindowScene
{
    public override string Name => "editor-hex";

    protected override string Project => EditorFixture.HexAdventure;
}

/// <summary>The Isle Hopper sample, whose start maps the editor shows in a new scene.</summary>
internal sealed class EditorSampleScene : EditorWindowScene
{
    public override string Name => "editor-sample";

    protected override string Project => EditorFixture.IsleHopper;
}

/// <summary>Play mode: the Game tab shows the running scene and the app bar is tinted.</summary>
internal sealed class EditorPlayScene : EditorWindowScene
{
    public override string Name => "editor-play";

    protected override void Customize(Window window)
    {
        var play = Editor.Get<IPlayModeService>();
        _ = play.PlayAsync();
        PlayWait.UntilSceneLoaded(play);
        RenderLoop.Settle(600);
    }
}

/// <summary>The command palette searching commands, entities and assets.</summary>
internal sealed class EditorPaletteScene : EditorWindowScene
{
    public override string Name => "editor-palette";

    protected override void Customize(Window window)
    {
        _ = Editor.Shell.ShowPaletteAsync("co");
        RenderLoop.Settle(300);
    }
}

/// <summary>The settings dialog on its keyboard page.</summary>
internal sealed class EditorSettingsScene : EditorWindowScene
{
    public override string Name => "editor-settings";

    protected override void Customize(Window window)
    {
        var settings = new SettingsDialogViewModel(Editor.Get<IDialogService>(), Editor.Get<ISettingsService>(), Editor.Get<UI.Theming.IThemeManager>(),
            Editor.Shell.Commands)
        { SelectedSectionIndex = 2 };
        _ = Editor.Get<IDialogService>().ShowAsync(settings);
        RenderLoop.Settle(300);
    }
}

/// <summary>The console with messages of every severity and source, collapsed duplicates and the details of an error.</summary>
internal sealed class EditorConsoleScene : EditorWindowScene
{
    public override string Name => "editor-console";

    protected override void Customize(Window window)
    {
        var console = Editor.Console;
        console.Info("Compiled 6 scripts in 412 ms", ConsoleSource.Script);
        for (var i = 0; i < 5; i++)
            console.Warning("Rigidbody2D on Crab has no collider, so it falls through the ground", ConsoleSource.Game);
        console.Info("Plugin samples.islehopper loaded (runtimeScene)", ConsoleSource.Plugin);
        console.Error("NullReferenceException in HeroMovement.OnUpdate", new InvalidOperationException("Object reference not set to an instance of an object."),
            ConsoleSource.Script, new FileTarget("scripts/HeroMovement.cs", 88));
        RenderLoop.Settle(200);
        var layout = Editor.Get<LayoutService>();
        layout.ShowPanel(PanelIds.Console);
        var viewModel = Editor.Get<ConsoleViewModel>();
        if (layout.Layout.FindPanel(PanelIds.Console) is { } group)
            layout.Layout.ToggleMaximize(group.Id);
        viewModel.SelectedRow = viewModel.Rows.LastOrDefault();
        RenderLoop.Settle(300);
    }
}

/// <summary>The project hub with recent projects and samples.</summary>
internal class HubScene : ScreenshotScene
{
    private ServiceProvider? _application;

    public override string Name => "editor-hub";

    public override Size Size => new(1160, 740);

    public override Control Build()
    {
        var settings = new JsonSettingsService(null);
        settings.Update(s =>
        {
            s.RecentProjects.Add(new RecentProject(EditorFixture.Platformer, "Coral Cove", DateTime.UtcNow.AddMinutes(-12)));
            s.RecentProjects.Add(new RecentProject(EditorFixture.HexAdventure, "Ember Isles", DateTime.UtcNow.AddDays(-2)));
            s.RecentProjects.Add(new RecentProject(Path.Combine(Path.GetTempPath(), "Moonlit Keep"), "Moonlit Keep", DateTime.UtcNow.AddDays(-40)));
        });
        _application = EditorFixture.CreateApplication(settings);
        var hub = _application.GetRequiredService<HubViewModel>();
        Configure(hub);
        return new HubView { DataContext = hub };
    }

    public override void Release()
    {
        if (_application is not null)
            EditorFixture.Dispose(_application);
        _application = null;
    }

    protected virtual void Configure(HubViewModel hub)
    {
    }
}

/// <summary>The hub's New project page with the templates.</summary>
internal sealed class HubNewProjectScene : HubScene
{
    public override string Name => "editor-hub-new";

    protected override void Configure(HubViewModel hub)
    {
        hub.Page = 1;
        hub.ProjectName = "Starfall";
        hub.Location = Path.Combine(Path.GetTempPath(), "Talesmith Projects");
    }
}
