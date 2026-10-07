using System.Numerics;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Prefabs;

/// <summary>Creates prefabs from entities, instantiates them, applies and reverts instance overrides, opens prefabs for editing and keeps instances
/// in the open scene up to date when prefab files change.</summary>
public sealed partial class PrefabWorkflow : ObservableObject, IEditorCommandContributor
{
    private const string Folder = "prefabs";

    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly ISelectionService _selection;
    private readonly IUndoService _undo;
    private readonly PrefabLibrary _library;
    private readonly PrefabInstances _instances;
    private readonly IConsole _console;
    private readonly IToastService _toasts;
    private readonly Viewport.ViewportCamera _camera;

    /// <summary>The scene to return to from the prefab being edited, or null for a new scene.</summary>
    [ObservableProperty]
    private string? _returnScene;

    public PrefabWorkflow(IProjectService project, ISceneDocumentService documents, ISelectionService selection, IUndoService undo, PrefabLibrary library,
        PrefabInstances instances, IConsole console, IToastService toasts, Viewport.ViewportCamera camera)
    {
        _project = project;
        _documents = documents;
        _selection = selection;
        _undo = undo;
        _library = library;
        _instances = instances;
        _console = console;
        _toasts = toasts;
        _camera = camera;
        _library.Changed += OnPrefabChanged;
        _documents.ActiveChanged += (_, _) => OnPropertyChanged(nameof(IsEditingPrefab));
        _documents.StateChanged += (_, _) => OnPropertyChanged(nameof(EditedName));
    }

    /// <summary>Whether the open document is a prefab rather than a scene.</summary>
    public bool IsEditingPrefab => PrefabDocuments.IsPrefabPath(_documents.Active?.Path);

    /// <summary>The name of the prefab being edited.</summary>
    public string EditedName => _documents.Active?.Title ?? "";

    /// <summary>The name of the scene <see cref="BackAsync"/> returns to.</summary>
    public string ReturnName => ReturnScene is { } path ? Path.GetFileNameWithoutExtension(path) : "Scene";

    /// <summary>Turns an entity and its children into a new prefab in <c>assets/prefabs</c> and makes the entity an instance of it.</summary>
    /// <returns>The new prefab's guid, or null when it could not be created.</returns>
    public async Task<AssetGuid?> CreatePrefabAsync(Guid entityId)
    {
        if (_documents.Active is not { } model || model.Find(entityId) is not { } entity)
            return null;
        var subtree = model.GetSubtree(entityId).Select(e => e.Clone()).ToList();
        subtree[0].Parent = null;
        if (subtree[0].FindComponent("Transform") is { } rootTransform)
            rootTransform.Data["position"] = new JsonArray(0, 0);
        var prefab = new PrefabDocument { Id = Guid.NewGuid(), Entities = subtree };
        var path = UniquePath(SafeName(entity.Name));
        if (await WriteAsync(path, prefab) is not { } guid)
            return null;
        if (!model.Contains(entityId))
            return guid;

        var instance = entity.Clone();
        instance.Prefab = new PrefabLink { Asset = guid };
        instance.Components = [.. entity.Components.Where(c => c.Type == "Transform").Select(c => c.Clone())];
        using (_undo.BeginTransaction($"Create prefab {EntityDataService.DisplayName(entity.Name)}"))
        {
            var children = model.GetChildren(entityId).Select(c => c.Id).ToList();
            if (children.Count > 0)
                model.DeleteEntities(children);
            model.ReplaceEntity(instance, $"Link {EntityDataService.DisplayName(entity.Name)} to its prefab");
        }

        _library.Store(guid, prefab);
        _console.Info($"Created prefab {path}", ConsoleSource.Editor);
        return guid;
    }

    /// <summary>Adds an instance of a prefab to the open scene; returns its id, or null when the prefab cannot be read.</summary>
    public Guid? Instantiate(AssetGuid prefab, Guid? parent, Vector2 position, int siblingIndex = -1)
    {
        if (_documents.Active is not { } model || _library.Get(prefab) is not { Root: { } root })
            return null;
        if (PrefabDocuments.IsPrefabPath(model.Path) && _project.Catalog.TryGetGuid(model.Path!, out var self) && self == prefab)
        {
            _toasts.Show("A prefab cannot contain itself", "Drop the prefab into a scene or another prefab.", ToastKind.Warning);
            return null;
        }

        var transform = (root.FindComponent("Transform")?.Data.DeepClone() as JsonObject) ?? [];
        transform["position"] = JsonFormats.WriteVector2(position);
        var instance = new EntityDocument
        {
            Id = Guid.NewGuid(),
            Name = UniqueName(model, string.IsNullOrWhiteSpace(root.Name) ? _library.GetName(prefab) : root.Name, parent),
            Prefab = new PrefabLink { Asset = prefab },
            Components = [new ComponentDocument("Transform", transform)]
        };
        var ids = model.InsertEntities([instance], parent is { } p && model.Contains(p) ? p : null, siblingIndex, $"Instantiate {_library.GetName(prefab)}");
        return ids.Count > 0 ? ids[0] : null;
    }

    /// <summary>Writes an instance's overrides, removed and added components into its prefab, then clears them from the instance.</summary>
    public async Task ApplyAsync(Guid instanceId)
    {
        if (_documents.Active is not { } model || model.Find(instanceId) is not { Prefab: { } link } instance || _library.Get(link.Asset) is not { } source ||
            _library.GetPath(link.Asset) is not { } path)
            return;
        var prefab = source.Clone();
        var expanded = PrefabExpansion.Expand(prefab.Entities, _library.Get).ToDictionary(e => e.Id);
        foreach (var change in link.Overrides)
        {
            if (!expanded.TryGetValue(change.Entity, out var target))
                continue;
            if (target.Source.InnerId is { } inner && prefab.FindEntity(target.Source.DocumentEntity) is { Prefab: { } nested })
            {
                nested.Overrides.RemoveAll(o => o.Entity == inner && o.Component == change.Component && o.Path == change.Path);
                nested.Overrides.Add(new PrefabOverride { Entity = inner, Component = change.Component, Path = change.Path, Value = change.Value?.DeepClone() });
            }
            else if (prefab.FindEntity(target.Source.DocumentEntity) is { } owner)
            {
                var component = owner.FindComponent(change.Component);
                if (component is null)
                    owner.Components.Add(component = new ComponentDocument(change.Component, []));
                JsonPaths.Set(component.Data, change.Path, change.Value?.DeepClone());
            }
        }

        foreach (var removed in link.RemovedComponents)
        {
            if (!expanded.TryGetValue(removed.Entity, out var target))
                continue;
            if (target.Source.InnerId is { } inner && prefab.FindEntity(target.Source.DocumentEntity) is { Prefab: { } nested })
                nested.RemovedComponents.Add(new PrefabComponentRef { Entity = inner, Component = removed.Component });
            else
                prefab.FindEntity(target.Source.DocumentEntity)?.Components.RemoveAll(c => c.Type == removed.Component);
        }

        if (prefab.Root is { } root)
        {
            foreach (var own in instance.Components.Where(c => c.Type != "Transform"))
            {
                var index = root.Components.FindIndex(c => c.Type == own.Type);
                if (index >= 0)
                    root.Components[index] = own.Clone();
                else
                    root.Components.Add(own.Clone());
            }
        }

        if (!await WriteFileAsync(path, prefab))
            return;
        if (model.Find(instanceId) is not null)
            model.ReplaceEntity(Cleared(model.Get(instanceId)), $"Apply overrides to {Path.GetFileNameWithoutExtension(path)}");
        _library.Store(link.Asset, prefab);
        _toasts.Show("Prefab updated", $"The changes were applied to {Path.GetFileName(path)}.", ToastKind.Success);
    }

    /// <summary>Drops every change of an instance against its prefab, keeping its place.</summary>
    public void Revert(Guid instanceId)
    {
        if (_documents.Active is not { } model || model.Find(instanceId) is not { Prefab: not null } instance)
            return;
        model.ReplaceEntity(Cleared(instance), $"Revert {EntityDataService.DisplayName(instance.Name)} to its prefab");
    }

    /// <summary>Opens a prefab for editing in place of the scene; <see cref="BackAsync"/> returns to the scene.</summary>
    public async Task OpenAsync(AssetGuid prefab)
    {
        if (_library.GetPath(prefab) is not { } path)
        {
            _toasts.Show("Prefab not found", "The prefab is not in the project.", ToastKind.Warning);
            return;
        }

        var current = _documents.Active?.Path;
        var returnTo = IsEditingPrefab ? ReturnScene : current;
        if (await _documents.OpenAsync(path))
            ReturnScene = returnTo;
    }

    /// <summary>Leaves the prefab being edited and opens the scene it was opened from.</summary>
    public async Task BackAsync()
    {
        if (!IsEditingPrefab)
            return;
        if (ReturnScene is { } scene)
        {
            if (await _documents.OpenAsync(scene))
                ReturnScene = null;
        }
        else if (await _documents.NewAsync())
        {
            ReturnScene = null;
        }
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string Category = "Prefab";
        bool HasStored() => _documents.Active is { } model && _selection.Entities.Any(model.Contains);
        bool HasInstance() => SelectedInstance() is not null;

        builder.Add("prefab.create", "Create prefab", Category, () => _ = CreateSelectedAsync(), HasStored, null, Icons.Package,
            "Saves the selected entities as prefabs in assets/prefabs and links them to it.");
        builder.Add("prefab.apply", "Apply prefab overrides", Category, () => _ = ApplyAsync(SelectedInstance()!.Value), HasInstance, null, Icons.Export,
            "Writes the selected instance's changes into its prefab.");
        builder.Add("prefab.revert", "Revert prefab overrides", Category, () => Revert(SelectedInstance()!.Value), HasInstance, null, Icons.RotateCcw,
            "Drops the selected instance's changes against its prefab.");
        builder.Add("prefab.open", "Open prefab", Category, () => _ = OpenAsync(SelectedPrefab()!.Value), () => SelectedPrefab() is not null, null, Icons.FolderOpen,
            "Edits the selected prefab or the prefab of the selected instance.");
        builder.Add("prefab.back", "Back to scene", Category, () => _ = BackAsync(), () => IsEditingPrefab, null, Icons.ArrowLeft,
            "Leaves the prefab and returns to the scene.");
        builder.Add("prefab.instantiate", "Instantiate prefab", Category, InstantiateSelectedAsset, () => SelectedPrefabAsset() is not null, null, Icons.Plus,
            "Adds the prefab selected in the assets to the scene.");
        foreach (var id in new[] { "prefab.create", "prefab.instantiate" })
            builder.Menu(MenuPaths.GameObject + "/Prefab", id, "create");
        foreach (var id in new[] { "prefab.open", "prefab.apply", "prefab.revert", "prefab.back" })
            builder.Menu(MenuPaths.GameObject + "/Prefab", id, "instance");
    }

    /// <summary>The prefab instance selected last, or null.</summary>
    public Guid? SelectedInstance() =>
        _documents.Active is { } model && _selection.Entities.Count > 0 && model.Find(_selection.Entities[^1]) is { Prefab: not null } entity ? entity.Id : null;

    private AssetGuid? SelectedPrefab()
    {
        if (SelectedPrefabAsset() is { } asset)
            return asset;
        if (_selection.Entities.Count > 0 && _instances.Find(_selection.Entities[^1]) is { } member)
            return member.Entity.Prefab.IsEmpty ? member.Instance.Prefab!.Asset : member.Entity.Prefab;
        return null;
    }

    private AssetGuid? SelectedPrefabAsset() =>
        _selection.Assets.Count > 0 && _library.GetPath(_selection.Assets[^1]) is { } path && PrefabDocuments.IsPrefabPath(path) ? _selection.Assets[^1] : null;

    private void InstantiateSelectedAsset()
    {
        if (SelectedPrefabAsset() is not { } prefab)
            return;
        var position = ViewCenter();
        if (Instantiate(prefab, null, position) is { } id)
            _selection.SelectEntity(id);
    }

    private Vector2 ViewCenter() => _camera.Position;

    private async Task CreateSelectedAsync()
    {
        if (_documents.Active is not { } model)
            return;
        foreach (var id in model.GetTopLevel(_selection.Entities).ToList())
            await CreatePrefabAsync(id);
    }

    private static EntityDocument Cleared(EntityDocument instance)
    {
        var cleared = instance.Clone();
        cleared.Prefab = new PrefabLink { Asset = instance.Prefab!.Asset, Extra = instance.Prefab.Extra };
        cleared.Components = [.. instance.Components.Where(c => c.Type == "Transform").Select(c => c.Clone())];
        return cleared;
    }

    private async Task<AssetGuid?> WriteAsync(string path, PrefabDocument prefab)
    {
        if (!await WriteFileAsync(path, prefab))
            return null;
        if (_project.Database is { } database)
        {
            try
            {
                await database.RefreshAsync([path]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _console.Warning($"The asset database did not pick up {path}: {ex.Message}", ConsoleSource.Assets);
            }
        }

        if (_project.Catalog.TryGetGuid(path, out var guid))
            return guid;
        _toasts.Show("Prefab saved but not registered", $"{path} was written, but the asset database does not know it yet.", ToastKind.Warning);
        return null;
    }

    private async Task<bool> WriteFileAsync(string path, PrefabDocument prefab)
    {
        try
        {
            var file = _project.Project.ToAbsolutePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await AtomicFile.WriteAllTextAsync(file, DocumentSerializer.Write(prefab));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _console.Error($"The prefab {path} could not be written: {ex.Message}", ex);
            _toasts.Show("Could not save the prefab", ex.Message, ToastKind.Error);
            return false;
        }
    }

    private string UniquePath(string name)
    {
        var path = $"{Folder}/{name}{PrefabDocuments.Extension}";
        for (var i = 2; File.Exists(_project.Project.ToAbsolutePath(path)); i++)
            path = $"{Folder}/{name} {i}{PrefabDocuments.Extension}";
        return path;
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string([.. (string.IsNullOrWhiteSpace(name) ? "Prefab" : name).Select(c => invalid.Contains(c) ? '_' : c)]).Trim();
        return safe.Length == 0 ? "Prefab" : safe;
    }

    private static string UniqueName(SceneDocumentModel model, string name, Guid? parent)
    {
        var names = model.GetChildren(parent).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
            return name;
        for (var i = 1; ; i++)
        {
            if (!names.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }

    private async void OnPrefabChanged(object? sender, PrefabChangedEventArgs e)
    {
        if (_documents.Active is not { } model)
            return;
        var ids = _instances.InstancesUsing(e.Prefab);
        if (ids.Count == 0)
            return;
        if (_library.GetPath(e.Prefab) is { } path && _project.EditSession?.Game.Services.GetService<IAssetManager>() is { } assets && assets.IsLoaded(path))
        {
            try
            {
                await Task.Run(() => assets.ReloadAsync(path));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _console.Warning($"The prefab {path} could not be reloaded: {ex.Message}", ConsoleSource.Assets);
            }
        }

        if (!ReferenceEquals(model, _documents.Active))
            return;
        foreach (var id in ids)
        {
            if (model.Find(id) is { } instance)
                model.RawReplace(instance.Clone());
        }
    }
}
