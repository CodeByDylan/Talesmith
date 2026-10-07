using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Console;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Shell;
using Talesmith.Scripting.Compiler;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Scripting;

/// <summary>The scripting commands (create a script, open the C# project, recompile) and the scripts' status bar item.</summary>
public sealed class ScriptCommands : IEditorCommandContributor
{
    public const string StatusItemId = "scripts";

    private readonly IScriptService _scripts;
    private readonly ICodeEditor _codeEditor;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IConsole _console;
    private readonly LayoutService _layout;
    private readonly ConsoleViewModel _consoleView;
    private readonly StatusBarItem _status;

    public ScriptCommands(IScriptService scripts, ScriptHotReloader hotReloader, EditSessionScripts editScripts, ICodeEditor codeEditor, IDialogService dialogs,
        IToastService toasts, IConsole console, LayoutService layout, ConsoleViewModel consoleView, StatusBarViewModel status)
    {
        ArgumentNullException.ThrowIfNull(hotReloader);
        ArgumentNullException.ThrowIfNull(editScripts);
        _scripts = scripts;
        _codeEditor = codeEditor;
        _dialogs = dialogs;
        _toasts = toasts;
        _console = console;
        _layout = layout;
        _consoleView = consoleView;
        _status = status.AddItem(StatusItemId, 80);
        _status.Command = new RelayCommand(ShowMessages);
        scripts.StateChanged += (_, _) => Refresh();
        Refresh();
    }

    public void Contribute(CommandBuilder builder)
    {
        const string Scripts = "Scripts";
        builder.Add("scripts.create", "Script…", Scripts, CreateAsync, null, null, Icons.FileCode, "Creates a C# script, system, component or class.");
        builder.Add("scripts.openProject", "Open C# project", Scripts, OpenProject, null, null, Icons.Code,
            "Opens the scripts in your code editor or IDE, with IntelliSense for the engine and plugins.");
        builder.Add("scripts.compile", "Recompile scripts", Scripts, async () => await _scripts.CompileAsync(), () => !_scripts.IsCompiling, null, Icons.Refresh,
            "Compiles the scripts now.");
        builder.Add("scripts.messages", "Show script messages", Scripts, ShowMessages, null, null, Icons.Terminal, "Shows the console filtered to scripts.");
        builder.Menu(MenuPaths.Assets + "/Create", "scripts.create", "code");
        builder.Menu(MenuPaths.Assets, "scripts.openProject", "scripts");
        builder.Menu(MenuPaths.Assets, "scripts.compile", "scripts");
    }

    /// <summary>Asks for a name and template, writes the script and opens it in the code editor.</summary>
    public async Task CreateAsync()
    {
        var options = _scripts.Options;
        if (await _dialogs.ShowAsync(new CreateScriptDialogViewModel(_dialogs, options.SourceRoot)) is not NewScript script)
            return;
        try
        {
            var path = ScriptTemplates.Create(options, script.Folder, script.ClassName, script.Template);
            _console.Info($"Created {Path.GetRelativePath(options.ProjectDirectory, path).Replace('\\', '/')}", ConsoleSource.Script,
                new FileTarget(path, 1));
            _toasts.Show("Script created", Path.GetFileName(path), ToastKind.Success);
            _codeEditor.OpenFile(path, 1);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not create the script", ex.Message, ToastKind.Error);
        }
    }

    private void OpenProject()
    {
        string project;
        try
        {
            project = _scripts.WriteProjectFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not write the C# project", ex.Message, ToastKind.Error);
            return;
        }

        if (!_codeEditor.OpenProject(project))
            _toasts.Show("No code editor found", $"Set one in Settings › Editor, or open {Path.GetFileName(project)} yourself.", ToastKind.Warning);
    }

    private void ShowMessages()
    {
        _layout.ShowPanel(PanelIds.Console);
        _consoleView.SourceIndex = (int)ConsoleSource.Script + 1;
    }

    private void Refresh()
    {
        var result = _scripts.LastResult;
        if (_scripts.IsCompiling)
        {
            _status.Kind = StatusKind.Busy;
            _status.Icon = Icons.Refresh;
            _status.Text = "Compiling scripts…";
        }
        else if (result is { Success: false })
        {
            var errors = result.Errors.Count();
            _status.Kind = StatusKind.Error;
            _status.Icon = Icons.AlertCircle;
            _status.Text = string.Create(CultureInfo.CurrentCulture, $"{errors} script {(errors == 1 ? "error" : "errors")}");
        }
        else if (result is { SourceFiles: 0 })
        {
            _status.Kind = StatusKind.Neutral;
            _status.Icon = Icons.Code;
            _status.Text = "No scripts";
        }
        else if (result is not null)
        {
            var warnings = result.Warnings.Count();
            _status.Kind = warnings > 0 ? StatusKind.Warning : StatusKind.Success;
            _status.Icon = Icons.Code;
            _status.Text = string.Create(CultureInfo.CurrentCulture, $"Scripts compiled in {result.Duration.TotalMilliseconds:N0} ms")
                           + (warnings > 0 ? string.Create(CultureInfo.CurrentCulture, $" · {warnings} {(warnings == 1 ? "warning" : "warnings")}") : "");
        }

        _status.ToolTip = "Show script messages in the console";
    }
}
