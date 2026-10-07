using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Editor.CommandPalette;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.UI;
using Talesmith.UI.Services;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Shell;

/// <summary>An app bar button for a command contributed with <see cref="CommandBuilder.Toolbar"/>.</summary>
public sealed record ToolbarButtonViewModel(EditorCommand Command)
{
    public string ToolTip => Command.GestureText is { } gesture ? $"{Command.Title} ({gesture})" : Command.Title;
}

/// <summary>The editor window: app bar, menus, play controls, toolbar, workspace and status bar, and the commands they run.</summary>
public sealed partial class ShellViewModel : ObservableObject, IEditorCommandContributor, IDisposable
{
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly ISettingsService _settings;
    private readonly IThemeManager _theme;
    private readonly IReadOnlyList<ICommandPaletteProvider> _paletteProviders;
    private readonly EditorPluginGuard _plugins;
    private readonly MenuItemViewModel _recentScenes = new() { Header = "Open recent", Icon = Icons.FolderOpen };
    private bool _closeApproved;
    private bool _updatingScene;

    [ObservableProperty]
    private string? _selectedScene;

    [ObservableProperty]
    private string? _selectedPreset;

    public ShellViewModel(
        EditorCommandRegistry commands,
        IEnumerable<IEditorCommandContributor> contributors,
        IProjectService project,
        ISceneDocumentService documents,
        IUndoService undo,
        IPlayModeService play,
        LayoutService layout,
        PanelRegistry panels,
        ToolManager tools,
        ViewportService viewport,
        ViewportGrid grid,
        StatusBarViewModel status,
        ProjectLoadingViewModel loading,
        IDialogService dialogs,
        IFileDialogService files,
        ISettingsService settings,
        IThemeManager theme,
        IEnumerable<ICommandPaletteProvider> paletteProviders,
        IEnumerable<Hosting.ICloseGuard> closeGuards,
        EditorPluginGuard plugins)
    {
        Commands = commands;
        Project = project;
        Documents = documents;
        Undo = undo;
        Play = play;
        Layout = layout;
        Panels = panels;
        Tools = tools;
        Viewport = viewport;
        Grid = grid;
        Status = status;
        Loading = loading;
        _dialogs = dialogs;
        _files = files;
        _settings = settings;
        _theme = theme;
        _plugins = plugins;
        _paletteProviders = [.. paletteProviders.Select(p => plugins.FindPlugin(p) is null ? p : new PluginPaletteProvider(p, plugins))];
        CloseGuards = [.. closeGuards];

        commands.Load(this);
        foreach (var contributor in contributors)
        {
            Func<EditorCommand, EditorCommand>? guarded = plugins.FindPlugin(contributor) is null
                ? null
                : c => c with { Command = new PluginCommand(c.Command, contributor, c.Title, plugins) };
            plugins.Run(contributor, "add its commands", () => commands.Load(contributor, guarded));
        }
        Menu = MainMenuBuilder.Build(commands);
        ToolbarButtons = [.. commands.ToolbarEntries.OrderBy(e => e.Order).Select(e => commands.Find(e.CommandId)).OfType<EditorCommand>().Select(c => new ToolbarButtonViewModel(c))];

        documents.StateChanged += (_, _) => OnDocumentStateChanged();
        documents.ActiveChanged += (_, _) => OnDocumentStateChanged();
        undo.Changed += (_, _) => RefreshCommands();
        play.StateChanged += (_, _) => OnPlayStateChanged();
        project.StatusChanged += (_, _) => RefreshScenes();
        tools.Context.Selection.Changed += (_, _) => RefreshCommands();
        layout.LayoutReplaced += (_, _) => OnLayoutReplaced();
        theme.Changed += OnThemeChanged;
        _selectedPreset = layout.CurrentPreset;
        layout.Layout.ActivatePanel(PanelIds.Scene);
        RefreshRecentScenes();
        RefreshScenes();
    }

    public EditorCommandRegistry Commands { get; }

    public IProjectService Project { get; }

    public ISceneDocumentService Documents { get; }

    public IUndoService Undo { get; }

    public IPlayModeService Play { get; }

    public LayoutService Layout { get; }

    public PanelRegistry Panels { get; }

    public ToolManager Tools { get; }

    public ViewportService Viewport { get; }

    public ViewportOptions ViewportOptions => Viewport.Options;

    /// <summary>The grid the viewport shows, which the toolbar's grid toggle shows or hides.</summary>
    public ViewportGrid Grid { get; }

    public StatusBarViewModel Status { get; }

    /// <summary>Covers the workspace while the project opens.</summary>
    public ProjectLoadingViewModel Loading { get; }

    public IReadOnlyList<Hosting.ICloseGuard> CloseGuards { get; }

    /// <summary>The main menu.</summary>
    public ObservableCollection<MenuItemViewModel> Menu { get; }

    public IReadOnlyList<ToolbarButtonViewModel> ToolbarButtons { get; }

    /// <summary>The project's scenes, for the scene selector.</summary>
    public ObservableCollection<string> Scenes { get; } = [];

    public IReadOnlyList<string> Presets => Layout.Presets;

    public string ProjectName => Project.Project.Name;

    public string DocumentTitle => Documents.Active?.FileName ?? "";

    public bool IsModified => Documents.Active?.IsDirty == true;

    public string WindowTitle => $"{(IsModified ? "● " : "")}{DocumentTitle} — {ProjectName} — Talesmith";

    public bool IsPlaying => Play.IsPlaying;

    public bool IsPaused => Play.IsPaused;

    public Geometry ThemeIcon => _theme.ActualVariant == ThemeVariant.Dark ? Icons.Sun : Icons.Moon;

    /// <summary>Raised when the shell asks its window to close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets a command by id for binding.</summary>
    public EditorCommand this[string id] => Commands.Get(id);

    /// <summary>Decides whether the window may close, asking every close guard first.</summary>
    public async Task<bool> CanCloseAsync()
    {
        if (_closeApproved)
            return true;
        foreach (var guard in CloseGuards)
        {
            if (!await _plugins.RunAsync(guard, "decide whether the editor may close", guard.CanCloseAsync, true))
                return false;
        }

        _closeApproved = true;
        return true;
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string File = "File";
        const string Edit = "Edit";
        const string Window = "Window";
        const string Help = "Help";

        builder.Add("file.newScene", "New scene", File, () => Documents.NewAsync(), null, "Ctrl+N", Icons.FilePlus);
        builder.Add("file.openScene", "Open scene…", File, () => Documents.OpenWithDialogAsync(), null, "Ctrl+O", Icons.FolderOpen);
        builder.Add("file.save", "Save scene", File, () => Documents.SaveAsync(), () => Documents.Active is not null, "Ctrl+S", Icons.Save);
        builder.Add("file.saveAs", "Save scene as…", File, () => Documents.SaveAsAsync(), () => Documents.Active is not null, "Ctrl+Shift+S", Icons.Save);
        builder.Add("file.revert", "Revert scene", File, Documents.RevertAsync, () => Documents.Active is { IsDirty: true, Path: not null }, null, Icons.RotateCcw);
        builder.Add("file.openProject", "Open project…", File, OpenProjectAsync, null, "Ctrl+Shift+O", Icons.FolderOpen);
        builder.Add("file.closeProject", "Close project", File, async () => await Project.CloseAsync(), null, null, Icons.X,
            "Closes the project and returns to the project hub.");
        builder.Add("file.showFolder", "Show project folder", File, () => Launch(Project.Project.Folder), null, null, Icons.Folder);
        builder.Add("file.exit", "Exit", File, () => CloseRequested?.Invoke(this, EventArgs.Empty), null, "Ctrl+Q", Icons.X);
        builder.Menu(MenuPaths.File, "file.newScene", "scene");
        builder.Menu(MenuPaths.File, "file.openScene", "scene");
        builder.Menu(MenuPaths.File, _recentScenes, "scene");
        builder.Menu(MenuPaths.File, "file.save", "save");
        builder.Menu(MenuPaths.File, "file.saveAs", "save");
        builder.Menu(MenuPaths.File, "file.revert", "save");
        builder.Menu(MenuPaths.File, "file.openProject", "project");
        builder.Menu(MenuPaths.File, "file.showFolder", "project");
        builder.Menu(MenuPaths.File, "file.closeProject", "project");
        builder.Menu(MenuPaths.File, "file.exit", "exit");

        builder.Add("edit.undo", "Undo", Edit, Undo.Undo, () => Undo.CanUndo, "Ctrl+Z", Icons.Undo);
        builder.Add(new EditorCommand("edit.redo", "Redo", Edit, new RelayCommand(Undo.Redo, () => Undo.CanRedo), KeyGestures.Parse("Ctrl+Shift+Z"), Icons.Redo)
        {
            AlternateGesture = KeyGestures.Parse("Ctrl+Y")
        });
        builder.Add("edit.palette", "Command palette…", Edit, () => ShowPaletteAsync(""), null, "Ctrl+K", Icons.Search, "Searches commands, entities and assets.");
        builder.Add("edit.settings", "Settings…", Edit, ShowSettingsAsync, null, "Ctrl+OemComma", Icons.Settings);
        builder.Add("edit.theme", "Toggle light/dark theme", Edit, ToggleTheme, null, "Ctrl+Shift+T", Icons.Moon);
        builder.Menu(MenuPaths.Edit, "edit.undo", "history");
        builder.Menu(MenuPaths.Edit, "edit.redo", "history");
        builder.Menu(MenuPaths.Edit, "edit.palette", "tools", 10);
        builder.Menu(MenuPaths.Edit, "edit.theme", "tools", 11);
        builder.Menu(MenuPaths.Edit, "edit.settings", "tools", 12);

        builder.Add("assets.find", "Find asset…", "Assets", () => ShowPaletteAsync("#"), null, "Ctrl+Shift+F", Icons.Search, "Finds and opens an asset by name.");
        builder.Add("assets.findEntity", "Go to entity…", "Assets", () => ShowPaletteAsync("@"), null, "Ctrl+Shift+K", Icons.Box, "Finds an entity of the open scene.");
        builder.Add("assets.rescan", "Refresh assets", "Assets", () => Project.RescanAsync(), () => Project.IsReady, null, Icons.Refresh,
            "Scans the asset folder again for changes made outside the editor.");
        builder.Add("assets.showFolder", "Show assets folder", "Assets", () => Launch(Project.Project.AssetRoot), null, null, Icons.FolderOpen);
        builder.Menu(MenuPaths.Assets, "assets.find", "find");
        builder.Menu(MenuPaths.Assets, "assets.findEntity", "find");
        builder.Menu(MenuPaths.Assets, "assets.rescan", "folder");
        builder.Menu(MenuPaths.Assets, "assets.showFolder", "folder");

        foreach (var panel in Panels.Panels)
        {
            var id = $"window.{panel.Id}";
            builder.Add(id, panel.Title, Window, () => Layout.ShowPanel(panel.Id), null, panel.Shortcut, panel.Icon, $"Shows the {panel.Title} panel.");
            builder.Menu(MenuPaths.Window + "/Panels", id, panel.Location.ToString(), panel.Order);
        }

        foreach (var preset in LayoutService.BuiltInPresets)
        {
            var id = $"window.layout.{preset.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()}";
            builder.Add(id, $"{preset} layout", Window, () => ApplyPreset(preset), null, null, Icons.Layout);
            builder.Menu(MenuPaths.Window + "/Layout", id, "presets");
        }

        builder.Add("window.saveLayout", "Save layout as…", Window, SaveLayoutAsync, null, null, Icons.Save);
        builder.Add("window.resetLayout", "Reset layout", Window, () => ApplyPreset(LayoutService.DefaultPreset), null, null, Icons.RotateCcw);
        builder.Menu(MenuPaths.Window + "/Layout", "window.saveLayout", "manage");
        builder.Menu(MenuPaths.Window + "/Layout", "window.resetLayout", "manage");

        builder.Add("help.shortcuts", "Keyboard shortcuts", Help, ShowShortcutsAsync, null, "F1", Icons.Keyboard);
        builder.Add("help.docs", "Editor guide", Help, () => Launch("https://talesmith.dev/guide"), null, null, Icons.Info);
        builder.Add("help.about", "About Talesmith", Help, ShowAboutAsync, null, null, Icons.Info);
        builder.Menu(MenuPaths.Help, "help.shortcuts", "help");
        builder.Menu(MenuPaths.Help, "help.docs", "help");
        builder.Menu(MenuPaths.Help, "help.about", "about");
    }

    /// <summary>Stops following the application's theme, which outlives the project and would otherwise keep this editor alive.</summary>
    public void Dispose() => _theme.Changed -= OnThemeChanged;

    private void OnThemeChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ThemeIcon));

    [RelayCommand]
    private async Task OpenRecentAsync(string path) => await Documents.OpenAsync(path);

    /// <summary>Opens the command palette with text already typed, such as a provider's prefix.</summary>
    public Task<object?> ShowPaletteAsync(string query)
    {
        var palette = new CommandPaletteViewModel(_dialogs, Commands, _paletteProviders, _settings) { Query = query };
        return _dialogs.ShowAsync(palette);
    }

    private Task<object?> ShowSettingsAsync() => _dialogs.ShowAsync(new SettingsDialogViewModel(_dialogs, _settings, _theme, Commands));

    private Task<object?> ShowShortcutsAsync() => _dialogs.ShowAsync(new ShortcutsDialogViewModel(_dialogs, Commands));

    private Task<object?> ShowAboutAsync() => _dialogs.ShowMessageAsync("Talesmith",
        $"A 2D game engine and editor for tile maps, sprites, particles, lighting and C# scripts.{Environment.NewLine}Version {typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3)}",
        [new MessageDialogButton("Close", null, IsDefault: true, IsCancel: true)]);

    private async Task SaveLayoutAsync()
    {
        var input = new TextInputDialogViewModel(_dialogs, "Save layout", "Name the layout to find it in the layout picker and the Window menu.", "Layout name",
            Layout.CurrentPreset is { } current && !LayoutService.BuiltInPresets.Contains(current) ? current : "My layout", "Save");
        if (await _dialogs.ShowAsync(input) is string name && !string.IsNullOrWhiteSpace(name))
        {
            Layout.SavePreset(name.Trim());
            OnPropertyChanged(nameof(Presets));
            SelectedPreset = name.Trim();
        }
    }

    private async Task OpenProjectAsync()
    {
        var folder = await _files.PickFolderAsync("Open project", Path.GetDirectoryName(Project.Project.Folder));
        if (folder is null)
            return;
        if (!EditorProject.TryResolve(folder, out var projectFolder))
        {
            await _dialogs.ShowMessageAsync("Not a project", $"\"{folder}\" has no assets/config/game.json.", [new MessageDialogButton("OK", null, IsDefault: true, IsCancel: true)]);
            return;
        }

        await Project.OpenAsync(projectFolder);
    }

    private void ApplyPreset(string preset)
    {
        Layout.ApplyPreset(preset);
        SelectedPreset = preset;
    }

    private void ToggleTheme()
    {
        var mode = _theme.ActualVariant == ThemeVariant.Dark ? ThemeMode.Light : ThemeMode.Dark;
        _theme.Mode = mode;
        _settings.Update(s => s.Theme = mode);
    }

    private static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
        }
    }

    partial void OnSelectedSceneChanged(string? value)
    {
        if (_updatingScene || value is null || Documents.Active?.Path is { } open && AssetPath.Comparer.Equals(open, value))
            return;
        _ = SwitchSceneAsync(value);
    }

    partial void OnSelectedPresetChanged(string? value)
    {
        if (value is not null && value != Layout.CurrentPreset)
            Layout.ApplyPreset(value);
    }

    private async Task SwitchSceneAsync(string path)
    {
        if (!await Documents.OpenAsync(path))
            SyncSelectedScene();
    }

    private void OnDocumentStateChanged()
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(WindowTitle));
        _closeApproved = false;
        RefreshRecentScenes();
        SyncSelectedScene();
        RefreshCommands();
    }

    private void OnPlayStateChanged()
    {
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(IsPaused));
        RefreshCommands();
        if (Play.IsPlaying || Play.State == PlayState.Starting)
            Layout.ShowPanel(PanelIds.Game);
        else if (Play.State == PlayState.Stopped)
            Layout.Layout.ActivatePanel(PanelIds.Scene);
    }

    private void OnLayoutReplaced()
    {
        if (SelectedPreset != Layout.CurrentPreset)
            SelectedPreset = Layout.CurrentPreset;
    }

    private void RefreshCommands() => Commands.RefreshCanExecute();

    private void RefreshRecentScenes()
    {
        _recentScenes.Items.Clear();
        foreach (var path in Documents.RecentScenes)
            _recentScenes.Items.Add(new MenuItemViewModel { Header = path, Command = OpenRecentCommand, CommandParameter = path, Icon = Icons.Clapperboard });
        if (_recentScenes.Items.Count == 0)
            _recentScenes.Items.Add(new MenuItemViewModel { Header = "No recent scenes" });
    }

    private void RefreshScenes()
    {
        if (Project.Database is not { IsScanned: true } database)
            return;
        var scenes = database.Assets.Where(a => a.Kind == AssetKind.Scene).Select(a => a.Path).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (Documents.Active?.Path is { } open && !scenes.Contains(open, AssetPath.Comparer))
            scenes.Add(open);
        if (scenes.SequenceEqual(Scenes))
            return;
        _updatingScene = true;
        Scenes.Clear();
        foreach (var scene in scenes)
            Scenes.Add(scene);
        _updatingScene = false;
        SyncSelectedScene();
    }

    private void SyncSelectedScene()
    {
        _updatingScene = true;
        if (Documents.Active?.Path is { } path && !Scenes.Contains(path))
            Scenes.Add(path);
        SelectedScene = Documents.Active?.Path;
        _updatingScene = false;
    }
}
