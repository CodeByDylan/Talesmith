using Talesmith.Editor.Commands;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>Edit › Project Settings.</summary>
public sealed class ProjectSettingsCommands(
    IProjectService project,
    IDialogService dialogs,
    IToastService toasts,
    EditorCommandRegistry commands,
    LayoutService layout) : IEditorCommandContributor
{
    public void Contribute(CommandBuilder builder)
    {
        builder.Add("edit.projectSettings", "Project Settings…", "Edit", ShowAsync, null, "Ctrl+Shift+OemComma", Icons.Sliders,
            "The game's title, window, start scene, timing, input actions and physics layers.");
        builder.Menu(MenuPaths.Edit, "edit.projectSettings", "tools", 13);
    }

    public Task<object?> ShowAsync() => dialogs.ShowAsync(Create());

    /// <summary>Creates the window's view model with its links to the build settings and the Plugins panel.</summary>
    public ProjectSettingsViewModel Create() => new(project, dialogs, toasts,
        () =>
        {
            commands.TryExecute("build.open");
            return Task.CompletedTask;
        },
        () => layout.ShowPanel(PanelIds.Plugins));
}
