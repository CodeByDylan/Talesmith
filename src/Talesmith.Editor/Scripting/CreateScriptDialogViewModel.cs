using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Scripting.Compiler;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Scripting;

/// <summary>What the Create Script dialog asks for.</summary>
/// <param name="Folder">The folder inside the scripts folder, or "" for the scripts folder itself.</param>
public sealed record NewScript(string ClassName, ScriptTemplate Template, string Folder);

/// <summary>A kind of file the Create Script dialog offers.</summary>
public sealed record ScriptTemplateOption(ScriptTemplate Template, string Title, string Description, Geometry Icon);

/// <summary>Asks for a class name, a template and a folder; the dialog's result is a <see cref="NewScript"/>, or null when cancelled.</summary>
public sealed partial class CreateScriptDialogViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly string _scriptsRoot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError), nameof(TargetPath))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _className = "NewScript";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError), nameof(TargetPath))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _folder = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTemplate))]
    private int _templateIndex;

    public CreateScriptDialogViewModel(IDialogService dialogs, string scriptsRoot, string folder = "")
    {
        _dialogs = dialogs;
        _scriptsRoot = scriptsRoot;
        _folder = folder;
    }

    public IReadOnlyList<ScriptTemplateOption> Templates { get; } =
    [
        new(ScriptTemplate.Script, "Script", "Behavior for one entity, with OnStart and Update.", Icons.FileCode),
        new(ScriptTemplate.System, "System", "An ECS system that updates every matching entity.", Icons.Cpu),
        new(ScriptTemplate.Component, "Component", "Data for entities, shown in the inspector.", Icons.Box),
        new(ScriptTemplate.Class, "Class", "A plain C# class.", Icons.Code)
    ];

    public ScriptTemplateOption SelectedTemplate => Templates[Math.Clamp(TemplateIndex, 0, Templates.Count - 1)];

    /// <summary>Why the name or folder cannot be used, or null.</summary>
    public string? Error
    {
        get
        {
            if (ScriptTemplates.ValidateClassName(ClassName.Trim()) is { } error)
                return error;
            if (Folder.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(Folder))
                return "The folder must be inside the scripts folder.";
            return File.Exists(Path.Combine(_scriptsRoot, Folder, ClassName.Trim() + ".cs")) ? $"{TargetPath} already exists." : null;
        }
    }

    public bool HasError => Error is not null;

    /// <summary>The new file's path relative to the asset folder.</summary>
    public string TargetPath => string.Join('/', new[] { "scripts", Folder.Trim('/', '\\').Replace('\\', '/'), ClassName.Trim() + ".cs" }.Where(p => p.Length > 0));

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => _dialogs.Close(this, new NewScript(ClassName.Trim(), SelectedTemplate.Template, Folder.Trim('/', '\\')));

    [RelayCommand]
    private void Cancel() => _dialogs.Close(this, null);

    private bool CanCreate() => !HasError;
}
