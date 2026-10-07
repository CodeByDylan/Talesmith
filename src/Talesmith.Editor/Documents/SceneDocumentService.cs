using System.Text.Json.Nodes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Editor.Console;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Settings;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Documents;

/// <summary>The default <see cref="ISceneDocumentService"/>, which also saves periodically when autosave is on.</summary>
public sealed class SceneDocumentService : ISceneDocumentService, ICloseGuard, IDisposable
{
    private const string LastSceneKey = "scenes.last";
    private const string RecentKey = "scenes.recent";
    private const int MaxRecent = 10;

    private static readonly FileFilter[] SceneFilters = [new("Scene", [".tscene"])];

    private readonly IProjectService _project;
    private readonly ProjectState _state;
    private readonly IUndoService _undo;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly IToastService _toasts;
    private readonly IConsole _console;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _autosave;
    private List<string> _recent;
    private bool _wasDirty;

    public SceneDocumentService(IProjectService project, ProjectState state, IUndoService undo, IDialogService dialogs, IFileDialogService files,
        IToastService toasts, IConsole console, ISettingsService settings)
    {
        _project = project;
        _state = state;
        _undo = undo;
        _dialogs = dialogs;
        _files = files;
        _toasts = toasts;
        _console = console;
        _settings = settings;
        _recent = state.Get<List<string>>(RecentKey) ?? [];
        _undo.Changed += OnUndoChanged;
        _autosave = new DispatcherTimer();
        _autosave.Tick += OnAutosaveTick;
        _settings.Changed += OnSettingsChanged;
        ConfigureAutosave();
    }

    public SceneDocumentModel? Active { get; private set; }

    public IReadOnlyList<string> RecentScenes => _recent;

    public bool IsLoading { get; private set; }

    public event EventHandler? ActiveChanged;

    public event EventHandler? StateChanged;

    public event EventHandler? Saved;

    private DocumentSerializer Serializer => _project.EditSession?.Game.Services.GetService<DocumentSerializer>() ?? DocumentSerializer.Default;

    public async Task<bool> NewAsync()
    {
        if (!await ConfirmDiscardAsync())
            return false;
        Activate(new SceneDocumentModel(CreateDefaultScene(), null, _undo));
        return true;
    }

    public async Task<bool> OpenAsync(string assetPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetPath);
        assetPath = AssetPath.Normalize(assetPath);
        if (Active?.Path is { } open && AssetPath.Comparer.Equals(open, assetPath))
            return true;
        if (!await ConfirmDiscardAsync())
            return false;
        var document = await ReadAsync(assetPath);
        if (document is null)
            return false;
        Activate(new SceneDocumentModel(document, assetPath, _undo));
        return true;
    }

    public Task<bool> OpenAsync(AssetGuid scene)
    {
        if (_project.Catalog.TryGetPath(scene, out var path))
            return OpenAsync(path);
        _toasts.Show("Scene not found", $"No scene in the project has the guid {scene}.", ToastKind.Warning);
        return Task.FromResult(false);
    }

    public async Task<bool> OpenWithDialogAsync()
    {
        var file = await _files.PickFileToOpenAsync("Open scene", SceneFilters, Path.Combine(_project.Project.AssetRoot, "scenes"));
        if (file is null)
            return false;
        if (_project.Project.ToAssetPath(file) is not { } assetPath)
        {
            _toasts.Show("Scene is outside the project", "Scenes must be inside the project's assets folder.", ToastKind.Warning);
            return false;
        }

        return await OpenAsync(assetPath);
    }

    public Task<bool> SaveAsync() => Active?.Path is null ? SaveAsAsync() : WriteAsync(Active, Active.Path);

    public async Task<bool> SaveAsAsync(string? assetPath = null)
    {
        if (Active is not { } model)
            return false;
        if (assetPath is null)
        {
            var file = await _files.PickFileToSaveAsync("Save scene as", model.FileName, SceneFilters);
            if (file is null)
                return false;
            assetPath = _project.Project.ToAssetPath(file);
            if (assetPath is null)
            {
                _toasts.Show("Scene must be inside the project", "Save scenes inside the project's assets folder so the game can load them.", ToastKind.Warning);
                return false;
            }
        }

        assetPath = AssetPath.Normalize(assetPath);
        if (!assetPath.EndsWith(".tscene", StringComparison.OrdinalIgnoreCase))
            assetPath += ".tscene";
        if (!await WriteAsync(model, assetPath))
            return false;
        model.Path = assetPath;
        Remember(assetPath);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task RevertAsync()
    {
        if (Active is not { Path: { } path } model || !model.IsDirty)
            return;
        if (!await _dialogs.ConfirmAsync("Revert scene?", $"Discard every unsaved change to \"{model.Title}\"?", "Revert", isDestructive: true))
            return;
        if (await ReadAsync(path) is not { } document)
            return;
        _undo.Clear(model);
        model.Reload(document);
        _undo.MarkSaved(model);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task OpenStartupSceneAsync()
    {
        try
        {
            await _project.WhenReady;
        }
        catch (Exception)
        {
            Activate(new SceneDocumentModel(CreateDefaultScene(), null, _undo));
            return;
        }

        foreach (var candidate in StartupCandidates())
        {
            if (File.Exists(_project.Project.ToAbsolutePath(candidate)) && await ReadAsync(candidate) is { } document)
            {
                Activate(new SceneDocumentModel(document, candidate, _undo));
                return;
            }
        }

        Activate(new SceneDocumentModel(CreateSceneFromStartMaps() ?? CreateDefaultScene(), null, _undo));
    }

    public async Task<bool> ConfirmDiscardAsync()
    {
        if (Active is not { IsDirty: true } model)
            return true;
        switch (await _dialogs.AskToSaveChangesAsync(model.FileName))
        {
            case UnsavedChangesChoice.Save:
                return await SaveAsync();
            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    Task<bool> ICloseGuard.CanCloseAsync() => ConfirmDiscardAsync();

    public void Dispose()
    {
        _autosave.Stop();
        _undo.Changed -= OnUndoChanged;
        _settings.Changed -= OnSettingsChanged;
    }

    /// <summary>A scene with a camera, as new scenes start.</summary>
    public static SceneDocument CreateDefaultScene()
    {
        var scene = SceneDocument.Create();
        scene.Entities.Add(new EntityDocument
        {
            Id = Guid.NewGuid(),
            Name = "Main Camera",
            Components =
            {
                new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }),
                new ComponentDocument("Camera", new JsonObject { ["zoom"] = 1 })
            }
        });
        return scene;
    }

    private IEnumerable<string> StartupCandidates()
    {
        if (_state.Get<string>(LastSceneKey) is { } last)
            yield return last;
        if (_project.Settings.StartScene is { Name: DocumentScene.SceneName } start && start.Get(DocumentScene.PathParameter) is { } path)
            yield return path;
        if (_project.Database is { } database)
        {
            foreach (var scene in database.Assets.Where(a => a.Kind == AssetKind.Scene).Select(a => a.Path).Order(StringComparer.OrdinalIgnoreCase))
                yield return scene;
        }
    }

    /// <summary>For projects that start with the built-in "map" scene, a scene showing the start maps, so the viewport is not empty.</summary>
    private SceneDocument? CreateSceneFromStartMaps()
    {
        if (_project.Settings.StartScene is not { Name: "map" } start || start.Get("map") is not { } maps)
            return null;
        var scene = CreateDefaultScene();
        var layer = 100;
        foreach (var map in maps.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!_project.Catalog.TryGetGuid(map, out var guid))
                continue;
            scene.Entities.Add(new EntityDocument
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(map),
                Components =
                {
                    new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }),
                    new ComponentDocument("TileMapRenderer", new JsonObject { ["map"] = guid.ToString(), ["renderLayer"] = layer })
                }
            });
            layer += 10;
        }

        return scene;
    }

    private async Task<SceneDocument?> ReadAsync(string assetPath)
    {
        var file = _project.Project.ToAbsolutePath(assetPath);
        IsLoading = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var serializer = Serializer;
            return await Task.Run(async () =>
            {
                await using var stream = File.OpenRead(file);
                return Prefabs.PrefabDocuments.IsPrefabPath(assetPath) ? Prefabs.PrefabDocuments.ToScene(serializer.ReadPrefab(stream)) : serializer.ReadScene(stream);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or FormatException)
        {
            _console.Error($"The scene {assetPath} could not be opened: {ex.Message}", ex, target: new AssetTarget(default, assetPath));
            _toasts.Show("Could not open the scene", ex.Message, ToastKind.Error);
            return null;
        }
        finally
        {
            IsLoading = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> WriteAsync(SceneDocumentModel model, string assetPath)
    {
        try
        {
            var json = Prefabs.PrefabDocuments.IsPrefabPath(assetPath) ? DocumentSerializer.Write(Prefabs.PrefabDocuments.ToPrefab(model.Document)) : DocumentSerializer.Write(model.Document);
            var file = _project.Project.ToAbsolutePath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await AtomicFile.WriteAllTextAsync(file, json);
            _undo.MarkSaved(model);
            Saved?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _console.Error($"The scene {assetPath} could not be saved: {ex.Message}", ex);
            _toasts.Show("Could not save the scene", ex.Message, ToastKind.Error);
            return false;
        }
    }

    private void Activate(SceneDocumentModel model)
    {
        if (Active is { } previous)
            _undo.Clear(previous);
        Active = model;
        _wasDirty = false;
        if (model.Path is { } path && !Prefabs.PrefabDocuments.IsPrefabPath(path))
            Remember(path);
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remember(string path)
    {
        _recent.RemoveAll(p => AssetPath.Comparer.Equals(p, path));
        _recent.Insert(0, path);
        if (_recent.Count > MaxRecent)
            _recent = _recent[..MaxRecent];
        _state.Set(RecentKey, _recent);
        _state.Set(LastSceneKey, path);
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ConfigureAutosave();

    private void OnUndoChanged(object? sender, EventArgs e)
    {
        var dirty = Active?.IsDirty == true;
        if (dirty == _wasDirty)
            return;
        _wasDirty = dirty;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigureAutosave()
    {
        var settings = _settings.Current;
        _autosave.Stop();
        if (!settings.Autosave)
            return;
        _autosave.Interval = TimeSpan.FromMinutes(Math.Clamp(settings.AutosaveMinutes, 1, 120));
        _autosave.Start();
    }

    private async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (Active is { IsDirty: true, Path: { } path } model && !_undo.IsInTransaction && await WriteAsync(model, path))
            _console.Info($"Autosaved {model.FileName}");
    }
}
