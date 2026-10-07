using Avalonia.Controls;
using Talesmith.Build;
using Talesmith.Build.Player;
using Talesmith.Editor.Build;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.ProjectSettings;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.Viewport;
using Talesmith.Screenshots.Capture;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The Build dialog while it publishes the Windows player for Hex Quest.</summary>
internal sealed class EditorBuildProgressScene : EditorWindowScene
{
    public override string Name => "editor-build-progress";

    protected override string Project => EditorFixture.HexQuest;

    protected override void Customize(Window window)
    {
        var builds = Editor.Get<BuildService>();
        builds.Player = new PublishingPlayer();
        var dialog = BuildDialog.Show(Editor, BuildTargets.WindowsX64);
        _ = dialog.BuildCommand.ExecuteAsync(null);
        RenderLoop.Wait(() => builds.Progress?.Detail?.StartsWith("Compiling", StringComparison.Ordinal) == true, 60000);
        RenderLoop.Settle(600);
    }

    /// <summary>Stands in for publishing the player, which takes a minute, and stops at a point that shows the progress.</summary>
    private sealed class PublishingPlayer : IPlayerRuntimeProvider
    {
        public async Task<string> GetAsync(PlayerRequest request, IBuildOutput output, CancellationToken cancellationToken)
        {
            output.Log(BuildLogLevel.Info, $"Publishing the {request.Target.DisplayName} player ({request.Profile.DisplayName}); later builds reuse it");
            await Task.Delay(120, cancellationToken);
            output.Progress(0.58, "Compiling Talesmith.Rendering");
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return "";
        }
    }
}

/// <summary>The Build dialog's report after a real Linux build of Hex Quest.</summary>
internal sealed class EditorBuildReportScene : EditorWindowScene
{
    public override string Name => "editor-build-report";

    protected override string Project => EditorFixture.HexQuest;

    protected override void Customize(Window window)
    {
        var dialog = BuildDialog.Show(Editor, BuildTargets.LinuxX64);
        _ = dialog.BuildCommand.ExecuteAsync(null);
        RenderLoop.Wait(() => dialog.IsReportPage, 600000);
        RenderLoop.Settle(600);
    }
}

internal static class BuildDialog
{
    public static BuildDialogViewModel Show(OpenEditor editor, BuildTarget target)
    {
        var dialog = new BuildDialogViewModel(editor.Get<IBuildService>(), editor.Get<IProjectService>(), editor.Get<ISceneDocumentService>(),
            editor.Get<IDialogService>(), editor.Get<IFileDialogService>(), editor.Get<ICodeEditor>());
        dialog.SelectTargetCommand.Execute(dialog.Targets.Single(t => t.Target == target));
        dialog.ProfileIndex = (int)BuildProfileKind.Release;
        dialog.Icon = "sprites/hero.png";
        _ = editor.Get<IDialogService>().ShowAsync(dialog);
        return dialog;
    }
}

/// <summary>The Plugins panel with Hex Quest's cutscene and gameplay plugins, one card expanded, beside the map at the zoom of the tile map
/// screenshots.</summary>
internal sealed class EditorPluginsScene : EditorWindowScene
{
    public override string Name => "editor-plugins";

    protected override string Project => EditorFixture.HexQuest;

    protected override void Customize(Window window)
    {
        var selection = Editor.Get<ISelectionService>();
        var maps = Editor.Get<TileMapEditor>();
        selection.SelectEntity(Editor.Get<ISceneDocumentService>().Active!.Entities.Last(e => e.FindComponent("TileMapRenderer") is not null).Id);
        RenderLoop.Wait(() => maps.Map is not null, 30000);
        Editor.Get<ViewportService>().FrameSelection(animate: false);
        Editor.Get<ViewportCamera>().ZoomTo(0.55f, animate: false);
        selection.Clear();
        maps.Close();
        Editor.Get<LayoutService>().ShowPanel(PanelIds.Plugins);
        RenderLoop.Settle(300);
        var panel = Editor.Get<PluginsPanel>();
        if (panel.Plugins.FirstOrDefault(p => p.Id == "samples.cutscenes") is { } cutscenes)
            cutscenes.IsExpanded = true;
        RenderLoop.Settle(300);
    }
}

/// <summary>Project Settings on the input page, recording a new key for an action.</summary>
internal sealed class EditorInputSettingsScene : EditorWindowScene
{
    public override string Name => "editor-project-input";

    protected override string Project => EditorFixture.HexQuest;

    protected override void Customize(Window window)
    {
        var settings = Open(Editor, 1);
        settings.Input.SelectedAction = settings.Input.Actions.First(a => a.Name == "Interact");
        RenderLoop.Settle(200);
        settings.Input.StartRecordingCommand.Execute(settings.Input.SelectedAction.Bindings[^1].Slots[0]);
        RenderLoop.Settle(300);
    }

    public static ProjectSettingsViewModel Open(OpenEditor editor, int section)
    {
        var settings = new ProjectSettingsViewModel(editor.Get<IProjectService>(), editor.Get<IDialogService>(), editor.Get<IToastService>(),
            () => Task.CompletedTask, () => { })
        { SelectedSectionIndex = section };
        _ = editor.Get<IDialogService>().ShowAsync(settings);
        return settings;
    }
}

/// <summary>Project Settings on the physics page with named layers and a collision matrix.</summary>
internal sealed class EditorPhysicsSettingsScene : EditorWindowScene
{
    public override string Name => "editor-project-physics";

    protected override void Customize(Window window)
    {
        var settings = EditorInputSettingsScene.Open(Editor, 2);
        string[] names = ["Player", "Enemies", "Pickups", "Triggers"];
        for (var i = 0; i < names.Length; i++)
            settings.Physics.Layers[i + 1].Name = names[i];
        settings.Physics.SetCollision(2, 2, false);
        settings.Physics.SetCollision(1, 4, false);
        settings.Physics.SetCollision(2, 3, false);
        RenderLoop.Settle(300);
    }
}

/// <summary>A script compile error in the console, filtered to scripts, with the status bar item.</summary>
internal sealed class EditorScriptErrorScene : EditorWindowScene
{
    public override string Name => "editor-script-error";

    protected override string Project => EditorFixture.BrokenScripts;

    protected override void Customize(Window window)
    {
        var scripts = Editor.Get<IScriptService>();
        RenderLoop.Wait(() => scripts.LastResult is not null && !scripts.IsCompiling, 120000);
        var layout = Editor.Get<LayoutService>();
        layout.ShowPanel(PanelIds.Console);
        var console = Editor.Get<ConsoleViewModel>();
        console.SourceIndex = (int)ConsoleSource.Script + 1;
        RenderLoop.Settle(200);
        console.SelectedRow = console.Rows.FirstOrDefault(r => r.Entry.Severity == ConsoleSeverity.Error && r.Entry.Target is FileTarget);
        RenderLoop.Settle(300);
    }
}
