using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Avalonia.Hosting;
using Talesmith.Editor.Console;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.Scripting;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Tests.Plugins;

public sealed class PluginsPanelTests
{
    [Fact]
    public void SwitchingAPluginOffSavesItAndReloadsTheProjectWhenConfirmed() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => TestPlugins.Write(folder, "fixture.weather", "Weather"));
        var project = new RecordingProject(editor.Get<IProjectService>());
        var dialogs = new AnsweringDialogs(true);
        var panel = new PluginsPanel(project, dialogs, new SilentToasts(), editor.Get<IConsole>(), editor.Get<PluginScaffold>(), editor.Get<EditorPluginGuard>());
        var card = Assert.Single(panel.Plugins);
        Assert.Equal("Weather", card.Name);
        Assert.True(card.IsLoaded, card.Reason);
        Assert.True(card.IsEnabled);
        Assert.Contains(card.Permissions, p => p.Name == "runtimeScene" && !p.IsUndeclared);

        card.IsEnabled = false;
        await editor.WaitAsync(() => project.Reloads == 1);

        var configuration = PluginConfiguration.Load(Path.Combine(project.Project.AssetRoot, GameSession.PluginConfigurationFile));
        Assert.Contains("fixture.weather", configuration.Disabled);
        Assert.Equal("Restart required", dialogs.LastTitle);
        Assert.True(panel.RestartRequired);
        var refreshed = Assert.Single(panel.Plugins);
        Assert.False(refreshed.IsEnabled);
        Assert.True(refreshed.ChangePending);
        Assert.True(refreshed.IsLoaded);

        refreshed.IsEnabled = true;
        await editor.WaitAsync(() => project.Reloads == 1 && !panel.RestartRequired);
        Assert.DoesNotContain("fixture.weather",
            PluginConfiguration.Load(Path.Combine(project.Project.AssetRoot, GameSession.PluginConfigurationFile)).Disabled);
    });

    [Fact]
    public void DecliningTheReloadKeepsTheProjectOpen() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => TestPlugins.Write(folder, "fixture.weather", "Weather"));
        var project = new RecordingProject(editor.Get<IProjectService>());
        var panel = new PluginsPanel(project, new AnsweringDialogs(false), new SilentToasts(), editor.Get<IConsole>(), editor.Get<PluginScaffold>(), editor.Get<EditorPluginGuard>());

        await panel.SetEnabledAsync(panel.Plugins[0], false);

        Assert.Equal(0, project.Reloads);
        Assert.True(panel.RestartRequired);
    });

    [Fact]
    public void NewPluginProjectsHaveAManifestProjectAndPluginClass() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var created = await editor.Get<PluginScaffold>().CreateAsync("Day Night Cycle");

        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(created.Manifest))!;
        Assert.Equal("fixture.day-night-cycle", manifest["id"]!.GetValue<string>());
        Assert.Equal("DayNightCycle.dll", manifest["assembly"]!.GetValue<string>());
        Assert.Contains("assets/plugins/day-night-cycle/", await File.ReadAllTextAsync(created.ProjectFile), StringComparison.Ordinal);
        Assert.Contains("public sealed class DayNightCyclePlugin : IPlugin", await File.ReadAllTextAsync(created.PluginClass), StringComparison.Ordinal);
    });

    private sealed class RecordingProject(IProjectService inner) : IProjectService
    {
        public int Reloads { get; private set; }

        public EditorProject Project => inner.Project;

        public GameSettings Settings => inner.Settings;

        public AssetCatalog Catalog => inner.Catalog;

        public AssetDatabase? Database => inner.Database;

        public AssetHotReload? HotReload => inner.HotReload;

        public PluginManager Plugins => inner.Plugins;

        public GameSession? EditSession => inner.EditSession;

        public bool IsReady => inner.IsReady;

        public AssetScanProgress? ScanProgress => inner.ScanProgress;

        public Task WhenReady => inner.WhenReady;

        public event EventHandler? StatusChanged
        {
            add => inner.StatusChanged += value;
            remove => inner.StatusChanged -= value;
        }

        public event EventHandler<EditSessionChangedEventArgs>? EditSessionChanged
        {
            add => inner.EditSessionChanged += value;
            remove => inner.EditSessionChanged -= value;
        }

        public Task<GameSession> CreateSessionAsync(GameSessionRequest request, CancellationToken cancellationToken = default) =>
            inner.CreateSessionAsync(request, cancellationToken);

        public Task ReplaceEditSessionAsync(ScriptAssembly? scripts, CancellationToken cancellationToken = default) =>
            inner.ReplaceEditSessionAsync(scripts, cancellationToken);

        public Task UpdateSettingsAsync(Action<JsonObject> change, CancellationToken cancellationToken = default) => inner.UpdateSettingsAsync(change, cancellationToken);

        public Task RescanAsync() => inner.RescanAsync();

        public Task<bool> OpenAsync(string projectFolder) => inner.OpenAsync(projectFolder);

        public Task<bool> CloseAsync() => inner.CloseAsync();

        public Task<bool> ReloadAsync()
        {
            Reloads++;
            return Task.FromResult(true);
        }
    }

    private sealed class AnsweringDialogs(object? answer) : IDialogService
    {
        public string? LastTitle { get; private set; }

        public Task<object?> ShowAsync(object content) => Task.FromResult<object?>(null);

        public Task<object?> ShowMessageAsync(string title, string message, IReadOnlyList<MessageDialogButton> buttons)
        {
            LastTitle = title;
            return Task.FromResult(answer);
        }

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false) => Task.FromResult(answer is true);

        public Task<UnsavedChangesChoice> AskToSaveChangesAsync(string documentTitle) => Task.FromResult(UnsavedChangesChoice.Discard);

        public void Close(object content, object? result)
        {
        }
    }

    private sealed class SilentToasts : IToastService
    {
        public void Show(string title, string? message = null, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
        {
        }
    }
}
