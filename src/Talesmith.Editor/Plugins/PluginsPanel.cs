using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Console;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Plugins;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Plugins;

/// <summary>The Plugins panel: the project's plugins with their state, permissions, dependencies and settings, and switches to turn them on
/// and off.</summary>
/// <remarks>Switching a plugin is saved to <c>config/plugins.json</c> at once; loaded plugins change when the project reloads, which the panel
/// offers to do.</remarks>
public sealed partial class PluginsPanel : ObservableObject, IEditorPanel
{
    private readonly IProjectService _project;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IConsole _console;
    private readonly PluginScaffold _scaffold;
    private readonly EditorPluginGuard _guard;
    private List<PluginCardViewModel> _all = [];

    [ObservableProperty]
    private string _filter = "";

    public PluginsPanel(IProjectService project, IDialogService dialogs, IToastService toasts, IConsole console, PluginScaffold scaffold, EditorPluginGuard guard)
    {
        _project = project;
        _dialogs = dialogs;
        _toasts = toasts;
        _console = console;
        _scaffold = scaffold;
        _guard = guard;
        var dispatcher = Dispatcher.UIThread;
        project.Plugins.Changed += (_, _) => dispatcher.Post(Refresh);
        guard.Changed += (_, _) => dispatcher.Post(Refresh);
        Refresh();
    }

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = [];

    public PluginManager Manager => _project.Plugins;

    public string Summary
    {
        get
        {
            var loaded = _all.Count(p => p.IsLoaded);
            var off = _all.Count(p => !p.IsEnabled);
            var failed = _all.Count(p => p.HasErrors || p.IsSkipped);
            var parts = new List<string> { $"{_all.Count} installed", $"{loaded} loaded" };
            if (off > 0)
                parts.Add($"{off} off");
            if (failed > 0)
                parts.Add($"{failed} with problems");
            return string.Join(" · ", parts);
        }
    }

    public bool IsEmpty => _all.Count == 0;

    /// <summary>Whether the loaded plugins differ from the installed and switched-on ones until the project reloads.</summary>
    public bool RestartRequired => _project.Plugins.RestartRequired;

    public string PluginsFolder => _project.Plugins.Options.PluginsDirectory;

    public Control CreateContent() => new PluginsView { DataContext = this };

    /// <summary>Switches a plugin on or off, saves it and asks whether to reload the project to apply it.</summary>
    public async Task SetEnabledAsync(PluginCardViewModel card, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(card);
        PluginChange change;
        try
        {
            change = enabled ? _project.Plugins.Enable(card.Id) : _project.Plugins.Disable(card.Id);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            card.SetEnabledQuietly(!enabled);
            _toasts.Show($"Could not switch {card.Name} {(enabled ? "on" : "off")}", ex.Message, ToastKind.Error);
            return;
        }

        _console.Info($"Switched the plugin {card.Name} {(enabled ? "on" : "off")}", ConsoleSource.Plugin);
        Refresh();
        if (!change.RestartRequired)
            return;
        var reload = await _dialogs.ShowMessageAsync("Restart required",
            $"{card.Name} {(enabled ? "loads" : "unloads")} when the project reloads. Reload the project now? Unsaved changes are saved or discarded first.",
            [new MessageDialogButton("Later", false, IsCancel: true), new MessageDialogButton("Reload now", true, IsDefault: true)]);
        if (reload is true)
            await _project.ReloadAsync();
    }

    [RelayCommand]
    private void Rescan()
    {
        try
        {
            _project.Plugins.Rescan();
        }
        catch (InvalidDataException ex)
        {
            _toasts.Show("The plugin configuration is not valid", ex.Message, ToastKind.Error);
        }
    }

    [RelayCommand]
    private async Task ReloadProjectAsync() => await _project.ReloadAsync();

    [RelayCommand]
    private void OpenPluginsFolder()
    {
        Directory.CreateDirectory(PluginsFolder);
        try
        {
            Process.Start(new ProcessStartInfo(PluginsFolder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    [RelayCommand]
    private async Task NewPluginAsync()
    {
        var input = new Dialogs.TextInputDialogViewModel(_dialogs, "New plugin",
            "Creates a plugin project with plugin.json, a project file and an IPlugin class. Building it installs the plugin into this game.",
            "Plugin name", "My Plugin", "Create");
        if (await _dialogs.ShowAsync(input) is not string name || string.IsNullOrWhiteSpace(name))
            return;
        try
        {
            var created = await _scaffold.CreateAsync(name);
            _console.Info($"Created the plugin project {created.ProjectFile}; build it with dotnet build to install it", ConsoleSource.Plugin,
                new FileTarget(created.PluginClass, 1));
            _toasts.Show("Plugin project created", $"Build {Path.GetFileName(created.ProjectFile)} to install it, then rescan.", ToastKind.Success);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not create the plugin", ex.Message, ToastKind.Error);
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void Refresh()
    {
        var manager = _project.Plugins;
        var scan = manager.Scan;
        var report = manager.Report;
        var faults = _guard.Faults.ToLookup(f => f.PluginId, StringComparer.Ordinal);
        _all =
        [
            .. scan.Plugins
                .Select(next => new PluginCardViewModel(
                    (next.Id is { } id ? report.Find(id) : null) ?? next, next, scan, manager.Configuration, [.. faults[next.Id ?? ""]], SetEnabledAsync))
                .OrderBy(card => card.HasErrors || card.IsSkipped ? 0 : 1)
                .ThenBy(card => card.Name, StringComparer.CurrentCultureIgnoreCase)
        ];
        ApplyFilter();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RestartRequired));
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        Plugins.Clear();
        foreach (var card in _all.Where(c => filter.Length == 0 || c.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                                                || c.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                                                || c.Description.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
            Plugins.Add(card);
    }
}
