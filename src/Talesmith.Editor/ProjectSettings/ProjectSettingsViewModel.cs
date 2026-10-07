using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Projects;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>The Project Settings window: the game's settings, input actions and physics layers, with links to the build settings and plugins.</summary>
/// <remarks>Changes are kept until Save writes <c>config/game.json</c>, <c>config/input.json</c> and <c>config/physics.json</c>.</remarks>
public sealed partial class ProjectSettingsViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly Func<Task> _openBuild;
    private readonly Action _openPlugins;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGameSection), nameof(IsInputSection), nameof(IsPhysicsSection), nameof(IsBuildSection), nameof(IsPluginsSection))]
    private int _selectedSectionIndex;

    public ProjectSettingsViewModel(IProjectService project, IDialogService dialogs, IToastService toasts, Func<Task> openBuild, Action openPlugins)
    {
        _dialogs = dialogs;
        _toasts = toasts;
        _openBuild = openBuild;
        _openPlugins = openPlugins;
        Project = project;
        Game = new GameSettingsPage(project);
        Input = new InputSettingsPage(Path.Combine(project.Project.AssetRoot, project.Settings.InputProfile));
        Physics = new PhysicsSettingsPage(project.Project.AssetRoot);
        foreach (var page in new INotifyPropertyChanged[] { Game, Input, Physics })
            page.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == "IsDirty")
                    OnPropertyChanged(nameof(IsDirty));
            };
    }

    public IProjectService Project { get; }

    public GameSettingsPage Game { get; }

    public InputSettingsPage Input { get; }

    public PhysicsSettingsPage Physics { get; }

    public IReadOnlyList<SettingsSection> Sections { get; } =
    [
        new("Game", Icons.Monitor),
        new("Input", Icons.Keyboard),
        new("Physics", Icons.Weight),
        new("Build", Icons.PackageExport),
        new("Plugins", Icons.Plug)
    ];

    public bool IsGameSection => SelectedSectionIndex == 0;

    public bool IsInputSection => SelectedSectionIndex == 1;

    public bool IsPhysicsSection => SelectedSectionIndex == 2;

    public bool IsBuildSection => SelectedSectionIndex == 3;

    public bool IsPluginsSection => SelectedSectionIndex == 4;

    public bool IsDirty => Game.IsDirty || Input.IsDirty || Physics.IsDirty;

    public string PluginSummary => Project.Plugins.Report.Summary;

    public string ConfigFolder => Path.Combine(Project.Project.AssetRoot, "config");

    /// <summary>Writes every changed page; returns false and keeps the window open when a page is not valid.</summary>
    public async Task<bool> SaveAsync()
    {
        try
        {
            await Game.SaveAsync();
            if (Input.IsDirty)
                Input.Save();
            if (Physics.IsDirty)
                Physics.Save();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not save the project settings", ex.Message, ToastKind.Error);
            return false;
        }

        OnPropertyChanged(nameof(IsDirty));
        _toasts.Show("Project settings saved", "Play mode and builds use them from now on.", ToastKind.Success);
        return true;
    }

    [RelayCommand]
    private async Task Save() => await SaveAsync();

    [RelayCommand]
    private void Revert()
    {
        Game.Revert();
        Input.Reset();
        Physics.Revert();
    }

    [RelayCommand]
    private async Task CloseAsync() => await TryCloseAsync();

    private async Task<bool> TryCloseAsync()
    {
        if (IsDirty)
        {
            var choice = await _dialogs.ShowMessageAsync("Save project settings?", "Some project settings changed. Save them before closing?",
            [
                new MessageDialogButton("Cancel", null, IsCancel: true),
                new MessageDialogButton("Don't save", false, IsDestructive: true),
                new MessageDialogButton("Save", true, IsDefault: true)
            ]);
            if (choice is null || choice is true && !await SaveAsync())
                return false;
        }

        _dialogs.Close(this, null);
        return true;
    }

    [RelayCommand]
    private async Task OpenBuildAsync()
    {
        if (await TryCloseAsync())
            await _openBuild();
    }

    [RelayCommand]
    private async Task OpenPluginsAsync()
    {
        if (await TryCloseAsync())
            _openPlugins();
    }

    [RelayCommand]
    private void ToggleAllLayers() => Physics.ShowAllLayers = !Physics.ShowAllLayers;
}
