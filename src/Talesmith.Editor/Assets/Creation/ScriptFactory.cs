using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Editor.Assets.Creation;

/// <summary>A C# script from a template, after asking for its class name; scripts outside the scripts folder go into it, since only it is compiled.</summary>
public sealed class ScriptFactory : IAssetFactory
{
    /// <summary>The asset folder whose scripts are compiled.</summary>
    public const string ScriptsFolder = "scripts";

    public string Title => "C# Script";

    public AssetKind Kind => AssetKind.Script;

    public string Group => "code";

    public bool AsksForName => true;

    public async Task<AssetRecord?> CreateAsync(AssetCreationContext context)
    {
        var folder = AssetPath.Comparer.Equals(context.Folder, ScriptsFolder) || AssetPath.IsWithin(context.Folder, ScriptsFolder) ? context.Folder : ScriptsFolder;
        var viewModel = new CreateScriptViewModel(folder, name => context.Project.Database?.TryGetAsset(AssetPath.Combine(folder, name + ".cs"), out _) == true);
        if (await context.Dialogs.ShowAsync(new CreateScriptView(), viewModel) is not true)
            return null;

        if (context.Project.Database is { } database && !database.TryGetAsset(folder, out _))
            await context.Operations.CreateFolderAsync(AssetPath.GetDirectory(folder), AssetPath.GetFileName(folder));
        var relative = folder.Length > ScriptsFolder.Length ? folder[(ScriptsFolder.Length + 1)..] : null;
        var source = ScriptTemplates.Render(viewModel.Template, viewModel.ClassName, ScriptTemplates.NamespaceFor(Path.GetFileName(context.Project.Project.Folder), relative));
        return await context.Operations.CreateFileAsync(folder, viewModel.ClassName + ".cs", Encoding.UTF8.GetBytes(source));
    }
}

/// <summary>Asks for the class name and template of a new script, checking the name as it is typed.</summary>
public sealed partial class CreateScriptViewModel : ObservableObject, IClosableDialog
{
    private readonly Func<string, bool> _exists;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(HasError), nameof(FileName))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _className = "NewScript";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemplateDescription))]
    private int _templateIndex;

    public CreateScriptViewModel(string folder, Func<string, bool> exists)
    {
        Folder = folder;
        _exists = exists;
    }

    public Action<object?>? Close { get; set; }

    public string Folder { get; }

    public IReadOnlyList<string> Templates { get; } = ["Script", "System", "Component", "Class"];

    public ScriptTemplate Template => (ScriptTemplate)Math.Clamp(TemplateIndex, 0, 3);

    public string TemplateDescription => Template switch
    {
        ScriptTemplate.System => "An ECS system that runs every frame over the entities of a query.",
        ScriptTemplate.Component => "A component struct that scenes and the inspector can use.",
        ScriptTemplate.Class => "An empty class.",
        _ => "Behavior you attach to entities, with OnStart and Update."
    };

    public string FileName => $"assets/{Folder}/{ClassName.Trim()}.cs";

    public string? Error => ScriptTemplates.ValidateClassName(ClassName.Trim()) ?? (_exists(ClassName.Trim()) ? $"{ClassName.Trim()}.cs already exists." : null);

    public bool HasError => Error is not null && ClassName.Length > 0;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        ClassName = ClassName.Trim();
        Close?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => Close?.Invoke(false);

    private bool CanConfirm() => Error is null;
}
