using System.Collections.Concurrent;
using System.Security.Cryptography;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Undo;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps;

/// <summary>The tile maps the editor changed: their unsaved state, saving them as .hexy files together with the scene, and keeping hot reload
/// from replacing a map the editor holds.</summary>
/// <remarks>
/// <para>A map is its own undo document, so <see cref="IUndoService.IsDirty"/> tells whether it has unsaved changes. Maps are saved whenever the
/// scene is, and the editor asks about unsaved maps before the project closes.</para>
/// <para>Writing a map changes its file, which hot reload would import again as a new instance. Changes whose content is what the editor
/// wrote, and changes to maps with unsaved edits, are not reloaded; the editor keeps its instance.</para>
/// </remarks>
public sealed partial class TileMapDocuments : ICloseGuard, IDisposable
{
    private readonly IProjectService _project;
    private readonly IUndoService _undo;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IConsole _console;
    private readonly ILogger<TileMapDocuments> _logger;
    private readonly Dictionary<TileMap, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<string, FileState> _files = new(AssetPath.Comparer);
    private IDisposable? _filter;
    private Task _saving = Task.CompletedTask;

    public TileMapDocuments(IProjectService project, ISceneDocumentService documents, IUndoService undo, IDialogService dialogs, IToastService toasts,
        IConsole console, ILogger<TileMapDocuments> logger)
    {
        _project = project;
        _undo = undo;
        _dialogs = dialogs;
        _toasts = toasts;
        _console = console;
        _logger = logger;
        undo.Changed += (_, _) => Refresh();
        documents.Saved += (_, _) => _ = SaveAllAsync();
        project.StatusChanged += (_, _) => AttachHotReload();
        AttachHotReload();
    }

    /// <summary>The maps the editor tracks, in the order they were first edited.</summary>
    public IReadOnlyCollection<TileMap> Maps => _entries.Keys;

    /// <summary>Whether any tracked map has unsaved changes.</summary>
    public bool HasUnsavedMaps => _entries.Keys.Any(IsDirty);

    /// <summary>Raised on the UI thread when a map's unsaved state changed or a save finished.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Starts tracking a map, such as when the editor targets it.</summary>
    public void Track(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (_entries.ContainsKey(map))
            return;
        _entries[map] = new Entry(map.Version);
        UpdateFileState(map);
    }

    public bool IsDirty(TileMap map) => _undo.IsDirty(map);

    /// <summary>Saves every tracked map with unsaved changes; returns false when one could not be written.</summary>
    public Task<bool> SaveAllAsync()
    {
        var task = SaveAllCoreAsync();
        _saving = task;
        return task;
    }

    /// <summary>Writes a map to its file in Hexy's format; returns false when it could not be written.</summary>
    public async Task<bool> SaveAsync(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Track(map);
        if (string.IsNullOrEmpty(map.Path))
            return false;
        var file = _project.Project.ToAbsolutePath(map.Path);
        var version = map.Version;
        try
        {
            using var buffer = new MemoryStream();
            var result = await HexyMapWriter.WriteAsync(map, buffer);
            var bytes = buffer.ToArray();
            var entry = _entries[map];
            entry.WrittenHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            UpdateFileState(map);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await AtomicFile.WriteAllBytesAsync(file, bytes);
            entry.CleanVersion = version;
            _undo.MarkSaved(map);
            if (map.Version != version)
                _undo.MarkDirty(map);
            foreach (var warning in result.Warnings)
                _console.Warning($"{map.Path}: {warning}", ConsoleSource.Assets);
            LogSaved(_logger, map.Path);
            Refresh();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _console.Error($"The tile map {map.Path} could not be saved: {ex.Message}", ex, ConsoleSource.Assets);
            _toasts.Show("Could not save the tile map", ex.Message, ToastKind.Error);
            return false;
        }
    }

    async Task<bool> ICloseGuard.CanCloseAsync()
    {
        await _saving;
        var dirty = _entries.Keys.Where(IsDirty).ToList();
        if (dirty.Count == 0)
            return true;
        var title = dirty.Count == 1 ? AssetPath.GetFileName(dirty[0].Path) : $"{dirty.Count} tile maps";
        return await _dialogs.AskToSaveChangesAsync(title) switch
        {
            UnsavedChangesChoice.Save => await SaveAllAsync(),
            UnsavedChangesChoice.Discard => true,
            _ => false
        };
    }

    public void Dispose() => _filter?.Dispose();

    private async Task<bool> SaveAllCoreAsync()
    {
        await _saving;
        var success = true;
        foreach (var map in _entries.Keys.Where(IsDirty).ToList())
            success &= await SaveAsync(map);
        return success;
    }

    /// <summary>Keeps maps whose undo steps were dropped, such as when the scene was reverted, marked unsaved when they differ from their file.</summary>
    private void Refresh()
    {
        List<TileMap>? orphaned = null;
        var changed = false;
        foreach (var (map, entry) in _entries)
        {
            var dirty = IsDirty(map);
            if (!dirty && map.Version != entry.CleanVersion && !HasSteps(map))
                (orphaned ??= []).Add(map);
            changed |= dirty != entry.WasDirty;
            entry.WasDirty = dirty;
            UpdateFileState(map);
        }

        foreach (var map in orphaned ?? [])
        {
            _entries[map].WasDirty = true;
            _undo.MarkDirty(map);
        }

        if (changed || orphaned is not null)
            StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool HasSteps(TileMap map)
    {
        foreach (var step in _undo.UndoSteps.Concat(_undo.RedoSteps))
        {
            if (UndoService.DocumentsOf(step).Contains(map, ReferenceEqualityComparer.Instance))
                return true;
        }

        return false;
    }

    private void UpdateFileState(TileMap map)
    {
        if (!string.IsNullOrEmpty(map.Path) && _entries.TryGetValue(map, out var entry))
            _files[map.Path] = new FileState(entry.WrittenHash, IsDirty(map));
    }

    private void AttachHotReload()
    {
        if (_filter is not null || _project.HotReload is not { } hotReload)
            return;
        _filter = hotReload.AddFilter(ShouldReload);
    }

    /// <summary>Runs on a background thread: lets hot reload replace only maps the editor neither wrote nor changed.</summary>
    private bool ShouldReload(AssetChange change)
    {
        if (!_files.TryGetValue(change.Path, out var state))
            return true;
        if (state.WrittenHash is { } hash && _project.Database?.TryGetAsset(change.Guid, out var record) == true && record.ContentHash == hash)
            return false;
        if (!state.IsDirty)
            return true;
        Dispatcher.UIThread.Post(() => _toasts.Show("Tile map changed on disk",
            $"{AssetPath.GetFileName(change.Path)} has unsaved edits in the editor, so they were kept. Saving replaces the file.", ToastKind.Warning));
        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved tile map {Path}")]
    private static partial void LogSaved(ILogger logger, string path);

    private sealed class Entry(long cleanVersion)
    {
        public long CleanVersion { get; set; } = cleanVersion;

        public string? WrittenHash { get; set; }

        public bool WasDirty { get; set; }
    }

    private sealed record FileState(string? WrittenHash, bool IsDirty);
}
