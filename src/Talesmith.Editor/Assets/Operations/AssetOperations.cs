using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Undo;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Assets.Operations;

/// <summary>Creates, renames, moves, duplicates, deletes and imports assets through the asset database, so .meta files stay with their
/// assets, and records each change as one undoable step.</summary>
/// <remarks>Operations run one at a time in the order they were asked for, including undo and redo. Failures are shown as toasts and
/// logged; the methods then return null or false rather than throwing.</remarks>
public sealed partial class AssetOperations(IProjectService project, IUndoService undo, IDialogService dialogs, IToastService toasts, ILogger<AssetOperations> logger) : IDisposable
{
    private readonly SemaphoreSlim _queue = new(1, 1);

    private AssetDatabase Database => project.Database ?? throw new InvalidOperationException("The project has no asset database yet.");

    /// <summary>Whether the database finished its first scan, so operations can run.</summary>
    public bool IsReady => project.Database is { IsScanned: true };

    /// <summary>A path in a folder that nothing uses yet: the name itself, or "name 1.ext", "name 2.ext" and so on.</summary>
    public string UniquePath(string folder, string name)
    {
        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        var candidate = AssetPath.Combine(folder, name);
        for (var index = 1; Exists(candidate); index++)
            candidate = AssetPath.Combine(folder, $"{stem} {index.ToString(CultureInfo.InvariantCulture)}{extension}");
        return candidate;
    }

    public Task<AssetRecord?> CreateFolderAsync(string parent, string name = "New Folder") =>
        RunAsync($"Create folder {name}", async () =>
        {
            var record = await Database.CreateFolderAsync(UniquePath(parent, name));
            await RecordCreatedAsync(record);
            return record;
        });

    /// <summary>Creates a file with a unique name in a folder.</summary>
    public Task<AssetRecord?> CreateFileAsync(string folder, string fileName, byte[] contents) =>
        RunAsync($"Create {fileName}", async () =>
        {
            var record = await Database.CreateFileAsync(UniquePath(folder, fileName), contents);
            await RecordCreatedAsync(record);
            return record;
        });

    /// <summary>Renames an asset; a name without the asset's extension keeps it.</summary>
    public Task<AssetRecord?> RenameAsync(AssetRecord asset, string newName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        newName = newName.Trim();
        var extension = asset.IsFolder ? "" : Path.GetExtension(asset.Name);
        if (extension.Length > 0 && !newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            newName += extension;
        if (newName.Length == 0 || string.Equals(newName, asset.Name, StringComparison.Ordinal))
            return Task.FromResult<AssetRecord?>(asset);
        return RunAsync($"Rename {asset.Name}", async () =>
        {
            var from = asset.Path;
            var record = await Database.RenameAsync(from, newName);
            await RecordAsync(new AsyncCommand($"Rename {asset.Name} to {newName}", this,
                () => MoveByGuidAsync(asset.Guid, record.Path), () => MoveByGuidAsync(asset.Guid, from)));
            return record;
        });
    }

    /// <summary>Moves assets into a folder; assets already there, and folders into themselves, are left alone.</summary>
    public Task<bool> MoveAsync(IReadOnlyList<AssetRecord> assets, string folder)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var moving = assets.Where(a => !AssetPath.Comparer.Equals(AssetPath.GetDirectory(a.Path), folder)
                                       && !(a.IsFolder && (AssetPath.Comparer.Equals(a.Path, folder) || AssetPath.IsWithin(folder, a.Path))))
            .ToList();
        moving.RemoveAll(a => moving.Any(other => other.IsFolder && AssetPath.IsWithin(a.Path, other.Path) && !ReferenceEquals(other, a)));
        if (moving.Count == 0)
            return Task.FromResult(false);
        var description = moving.Count == 1 ? $"Move {moving[0].Name}" : $"Move {moving.Count} assets";
        return RunAsync(description, async () =>
        {
            var moves = new List<(AssetGuid Guid, string From, string To)>();
            foreach (var asset in moving)
            {
                var target = UniquePath(folder, asset.Name);
                await Database.MoveAsync(asset.Path, target);
                moves.Add((asset.Guid, asset.Path, target));
            }

            await RecordAsync(new AsyncCommand(description, this,
                async () =>
                {
                    foreach (var move in moves)
                        await MoveByGuidAsync(move.Guid, move.To);
                },
                async () =>
                {
                    foreach (var move in Enumerable.Reverse(moves))
                        await MoveByGuidAsync(move.Guid, move.From);
                }));
            return true;
        });
    }

    /// <summary>Copies assets next to the originals with new guids; returns the copies.</summary>
    public Task<IReadOnlyList<AssetRecord>> DuplicateAsync(IReadOnlyList<AssetRecord> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (assets.Count == 0)
            return Task.FromResult<IReadOnlyList<AssetRecord>>([]);
        var description = assets.Count == 1 ? $"Duplicate {assets[0].Name}" : $"Duplicate {assets.Count} assets";
        return RunListAsync(description, async () =>
        {
            var copies = new List<AssetRecord>();
            foreach (var asset in assets)
                copies.Add(await Database.DuplicateAsync(asset.Path));
            await RecordCreatedAsync(description, copies);
            return copies;
        });
    }

    /// <summary>Moves assets to the project's trash after asking; undo puts them back.</summary>
    public async Task<bool> DeleteAsync(IReadOnlyList<AssetRecord> assets, bool confirm = true)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var deleting = assets.Where(a => !assets.Any(other => other.IsFolder && !ReferenceEquals(other, a) && AssetPath.IsWithin(a.Path, other.Path))).ToList();
        if (deleting.Count == 0)
            return false;
        if (confirm)
        {
            var what = deleting.Count == 1 ? $"\"{deleting[0].Name}\"" : $"{deleting.Count} assets";
            var dependents = deleting.Sum(a => Database.GetDependents(a.Guid).Count(d => deleting.All(x => x.Guid != d)));
            var message = $"{what} will be moved to the project's trash. You can undo this.";
            if (dependents > 0)
                message += $" {dependents} other {(dependents == 1 ? "asset refers" : "assets refer")} to {(deleting.Count == 1 ? "it" : "them")}.";
            if (!await dialogs.ConfirmAsync(deleting.Count == 1 ? "Delete asset?" : "Delete assets?", message, "Delete", isDestructive: true))
                return false;
        }

        var description = deleting.Count == 1 ? $"Delete {deleting[0].Name}" : $"Delete {deleting.Count} assets";
        var result = await RunAsync(description, async () =>
        {
            var entries = new List<Trashed>();
            foreach (var asset in deleting)
                entries.Add(new Trashed(asset.Guid, asset.Path, await Database.DeleteAsync(asset.Path)));
            if (entries.All(e => e.TrashPath is not null))
                await RecordAsync(new AsyncCommand(description, this, () => DeleteAgainAsync(entries), () => RestoreAsync(entries)));
            return true;
        });
        return result;
    }

    /// <summary>Copies files or folders from outside the project into a folder; returns the imported assets.</summary>
    public Task<IReadOnlyList<AssetRecord>> ImportAsync(IReadOnlyList<string> files, string folder)
    {
        ArgumentNullException.ThrowIfNull(files);
        var sources = files.Where(f => File.Exists(f) || Directory.Exists(f)).ToList();
        if (sources.Count == 0)
            return Task.FromResult<IReadOnlyList<AssetRecord>>([]);
        var description = sources.Count == 1 ? $"Import {Path.GetFileName(sources[0])}" : $"Import {sources.Count} files";
        return RunListAsync(description, async () =>
        {
            var imported = new List<AssetRecord>();
            foreach (var source in sources)
            {
                var inside = project.Project.ToAssetPath(source);
                if (inside is not null)
                    continue;
                imported.Add(await Database.ImportAsync(source, UniquePath(folder, Path.GetFileName(Path.TrimEndingDirectorySeparator(source)))));
            }

            await RecordCreatedAsync(description, imported);
            if (imported.Count > 0)
                toasts.Show(imported.Count == 1 ? $"Imported {imported[0].Name}" : $"Imported {imported.Count} assets", kind: ToastKind.Success);
            return imported;
        });
    }

    /// <summary>Imports assets again in the games the editor runs, such as after changing files outside the editor.</summary>
    public async Task ReimportAsync(IReadOnlyList<AssetRecord> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        if (project.EditSession?.Game.Services.GetService(typeof(IAssetManager)) is not IAssetManager manager)
            return;
        var reloaded = 0;
        foreach (var asset in assets.Where(a => !a.IsFolder))
        {
            try
            {
                if (await Task.Run(() => manager.ReloadAsync(asset.Path)))
                    reloaded++;
                await Database.MarkImportedAsync(asset.Guid);
            }
            catch (Exception ex) when (ex is AssetException or IOException)
            {
                toasts.Show($"{asset.Name} could not be imported", ex.Message, ToastKind.Error);
            }
        }

        toasts.Show(reloaded == 0 ? "Nothing to reimport" : $"Reimported {reloaded} {(reloaded == 1 ? "asset" : "assets")}",
            reloaded == 0 ? "The assets are not loaded by the scene." : null, ToastKind.Success);
    }

    /// <summary>Stores new import settings of an asset as one undoable step, and reimports it.</summary>
    public Task<AssetRecord?> SetImportSettingsAsync<TSettings>(AssetRecord asset, TSettings settings) where TSettings : class
    {
        ArgumentNullException.ThrowIfNull(asset);
        return RunAsync($"Change import settings of {asset.Name}", async () =>
        {
            var before = asset.Meta;
            var record = await Database.SetImportSettingsAsync(asset.Path, settings);
            var after = record.Meta;
            await RecordAsync(new AsyncCommand($"Change import settings of {asset.Name}", this,
                () => SetMetaAsync(asset.Guid, after), () => SetMetaAsync(asset.Guid, before)));
            return record;
        });
    }

    /// <summary>Replaces the contents of an asset file, such as an edited sprite atlas, as one undoable step.</summary>
    public Task<AssetRecord?> WriteFileAsync(AssetRecord asset, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(contents);
        return RunAsync($"Change {asset.Name}", async () =>
        {
            var before = await File.ReadAllBytesAsync(Path.Combine(Database.RootFolder, asset.Path.Replace('/', Path.DirectorySeparatorChar)));
            var record = await Database.WriteFileAsync(asset.Path, contents);
            await RecordAsync(new AsyncCommand($"Change {asset.Name}", this, () => WriteByGuidAsync(asset.Guid, contents), () => WriteByGuidAsync(asset.Guid, before)));
            return record;
        });
    }

    /// <summary>Replaces an asset's labels as one undoable step.</summary>
    public Task<AssetRecord?> SetLabelsAsync(AssetRecord asset, IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var cleaned = labels.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return RunAsync($"Label {asset.Name}", async () =>
        {
            var before = asset.Meta;
            var record = await Database.UpdateMetaAsync(asset.Path, meta => meta with { Labels = cleaned });
            var after = record.Meta;
            await RecordAsync(new AsyncCommand($"Change labels of {asset.Name}", this, () => SetMetaAsync(asset.Guid, after), () => SetMetaAsync(asset.Guid, before)));
            return record;
        });
    }

    /// <summary>Opens the system's file manager at an asset.</summary>
    public void RevealInFileManager(AssetRecord? asset)
    {
        var path = asset is null ? Database.RootFolder : Path.Combine(Database.RootFolder, asset.Path.Replace('/', Path.DirectorySeparatorChar));
        var folder = asset is null || asset.IsFolder ? path : Path.GetDirectoryName(path)!;
        try
        {
            if (OperatingSystem.IsWindows() && asset is { IsFolder: false })
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            toasts.Show("The file manager could not be opened", ex.Message, ToastKind.Error);
        }
    }

    public void Dispose() => _queue.Dispose();

    private bool Exists(string path)
    {
        var full = Path.Combine(Database.RootFolder, path.Replace('/', Path.DirectorySeparatorChar));
        return Database.TryGetAsset(path, out _) || File.Exists(full) || Directory.Exists(full);
    }

    private Task RecordAsync(IUndoableCommand command) => Dispatcher.UIThread.InvokeAsync(() => undo.Record(command)).GetTask();

    private Task RecordCreatedAsync(AssetRecord record) => RecordCreatedAsync($"Create {record.Name}", [record]);

    private async Task RecordCreatedAsync(string description, List<AssetRecord> records)
    {
        if (records.Count == 0)
            return;
        var entries = records.Select(r => new Trashed(r.Guid, r.Path, null)).ToList();
        await RecordAsync(new AsyncCommand(description, this, () => RestoreAsync(entries), () => DeleteAgainAsync(entries)));
    }

    private async Task MoveByGuidAsync(AssetGuid guid, string path)
    {
        if (Database.TryGetAsset(guid, out var record) && !string.Equals(record.Path, path, StringComparison.Ordinal))
            await Database.MoveAsync(record.Path, path);
    }

    private async Task WriteByGuidAsync(AssetGuid guid, byte[] contents)
    {
        if (Database.TryGetAsset(guid, out var record))
            await Database.WriteFileAsync(record.Path, contents);
    }

    private async Task SetMetaAsync(AssetGuid guid, AssetMeta meta)
    {
        if (Database.TryGetAsset(guid, out var record))
            await Database.UpdateMetaAsync(record.Path, _ => meta);
    }

    private async Task DeleteAgainAsync(List<Trashed> entries)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (Database.TryGetAsset(entries[i].Guid, out var record))
                entries[i] = entries[i] with { Path = record.Path, TrashPath = await Database.DeleteAsync(record.Path) };
        }
    }

    private async Task RestoreAsync(List<Trashed> entries)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].TrashPath is { } trashed && !Database.TryGetAsset(entries[i].Guid, out _))
            {
                await Database.RestoreAsync(trashed, entries[i].Path);
                entries[i] = entries[i] with { TrashPath = null };
            }
        }
    }

    private async Task<T?> RunAsync<T>(string description, Func<Task<T>> operation)
    {
        await _queue.WaitAsync();
        try
        {
            return await Task.Run(operation);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or InvalidOperationException or AssetException)
        {
            LogFailed(logger, ex, description);
            toasts.Show($"{description} failed", ex.Message, ToastKind.Error);
            return default;
        }
        finally
        {
            _queue.Release();
        }
    }

    private async Task<IReadOnlyList<AssetRecord>> RunListAsync(string description, Func<Task<IReadOnlyList<AssetRecord>>> operation) =>
        await RunAsync(description, operation) ?? [];

    internal Task RunQueuedAsync(string description, Func<Task> operation) =>
        RunAsync(description, async () =>
        {
            await operation();
            return true;
        });

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed")]
    private static partial void LogFailed(ILogger logger, Exception exception, string operation);

    private sealed record Trashed(AssetGuid Guid, string Path, string? TrashPath);

    /// <summary>An undo step whose work runs asynchronously in the operation queue.</summary>
    private sealed class AsyncCommand(string description, AssetOperations owner, Func<Task> apply, Func<Task> revert) : IUndoableCommand
    {
        public string Description { get; } = description;

        public object? Document => null;

        public void Apply() => _ = owner.RunQueuedAsync($"Redo {Description}", apply);

        public void Revert() => _ = owner.RunQueuedAsync($"Undo {Description}", revert);
    }
}
