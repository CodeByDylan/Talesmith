using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Shell;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Build;

/// <summary>The Build commands, the app bar button and the status bar item that follows a build in the background.</summary>
public sealed class BuildCommands : IEditorCommandContributor
{
    public const string StatusItemId = "build";

    private readonly IBuildService _builds;
    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly ICodeEditor _codeEditor;
    private readonly StatusBarItem _status;
    private BuildDialogViewModel? _open;

    public BuildCommands(IBuildService builds, IProjectService project, ISceneDocumentService documents, IDialogService dialogs, IFileDialogService files,
        ICodeEditor codeEditor, StatusBarViewModel status)
    {
        _builds = builds;
        _project = project;
        _documents = documents;
        _dialogs = dialogs;
        _files = files;
        _codeEditor = codeEditor;
        _status = status.AddItem(StatusItemId, 70);
        _status.IsVisible = false;
        _status.Command = new AsyncRelayCommand(ShowAsync);
        builds.StateChanged += (_, _) => Refresh();
    }

    public void Contribute(CommandBuilder builder)
    {
        const string Build = "Build";
        builder.Add("build.open", "Build settings…", Build, ShowAsync, null, null, Icons.PackageExport,
            "Chooses the platform, profile, scenes and content of builds.");
        builder.Add("build.build", "Build", Build, () => BuildAsync(run: false), () => !_builds.IsBuilding, "Ctrl+Shift+B", Icons.Hammer,
            "Exports the game with the saved build settings.");
        builder.Add("build.run", "Build and Run", Build, () => BuildAsync(run: true), () => !_builds.IsBuilding, "Ctrl+Alt+B", Icons.Play,
            "Exports the game and starts it.");
        builder.Menu(MenuPaths.File, "build.open", "build");
        builder.Menu(MenuPaths.File, "build.build", "build");
        builder.Menu(MenuPaths.File, "build.run", "build");
        builder.Toolbar("build.open", 10);
    }

    /// <summary>Shows the Build dialog, on the progress or report of a build when one runs or just finished.</summary>
    public Task ShowAsync() => ShowAsync(start: null);

    private Task BuildAsync(bool run) => ShowAsync(start: run);

    private async Task ShowAsync(bool? start)
    {
        if (_open is { } open)
        {
            if (start is { } runAfter && !_builds.IsBuilding)
                await (runAfter ? open.BuildAndRunCommand : open.BuildCommand).ExecuteAsync(null);
            return;
        }

        var model = new BuildDialogViewModel(_builds, _project, _documents, _dialogs, _files, _codeEditor);
        _open = model;
        if (start is null && !_builds.IsBuilding && _status.IsVisible && _builds.LastReport is { } last)
            model.ShowReport(last);
        var shown = _dialogs.ShowAsync(model);
        if (start is { } run)
            _ = run ? model.BuildAndRunCommand.ExecuteAsync(null) : model.BuildCommand.ExecuteAsync(null);
        try
        {
            await shown;
        }
        finally
        {
            _open = null;
        }
    }

    private void Refresh()
    {
        if (_builds.IsBuilding)
        {
            _status.IsVisible = true;
            _status.Kind = StatusKind.Busy;
            _status.Icon = Icons.Refresh;
            var percent = (_builds.Progress?.Overall ?? 0).ToString("P0", CultureInfo.CurrentCulture);
            _status.Text = $"Building {percent}";
            _status.ToolTip = _builds.Progress is { } progress ? $"{progress.Title}{(progress.Detail is { } detail ? $": {detail}" : "")}" : null;
            return;
        }

        if (_builds.LastReport is not { } report || report.Cancelled)
        {
            _status.IsVisible = false;
            return;
        }

        _status.IsVisible = true;
        _status.Kind = report.Success ? StatusKind.Success : StatusKind.Error;
        _status.Icon = report.Success ? Icons.PackageExport : Icons.AlertCircle;
        _status.Text = report.Success ? $"Built in {BuildFormat.Duration(report.Duration)}" : "Build failed";
        _status.ToolTip = "Show the build report";
    }
}
