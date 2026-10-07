using System.Globalization;

namespace Talesmith.Assets.Database;

public sealed partial class AssetDatabase
{
    /// <summary>Creates a folder and its .meta file; missing parent folders are created too.</summary>
    /// <exception cref="IOException">Something already exists at the path.</exception>
    public Task<AssetRecord> CreateFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = ValidatePath(path);
        return RunAsync(async () =>
        {
            var fullPath = ToFullPath(normalized);
            if (File.Exists(fullPath) || Directory.Exists(fullPath))
                throw new IOException($"'{normalized}' already exists.");

            var created = new List<string>();
            for (var folder = normalized; folder.Length > 0 && !Directory.Exists(ToFullPath(folder)); folder = AssetPath.GetDirectory(folder))
                created.Insert(0, folder);
            Directory.CreateDirectory(fullPath);
            foreach (var folder in created)
                await AssetMetaFile.WriteAsync(ToFullPath(folder) + AssetMetaFile.Extension, AssetMeta.CreateFolder(AssetGuid.NewGuid()), cancellationToken).ConfigureAwait(false);
            return [created[0]];
        }, normalized, cancellationToken);
    }

    /// <summary>Moves an asset or folder, with its .meta file, to a new path in an existing folder.</summary>
    /// <exception cref="FileNotFoundException">Nothing exists at <paramref name="from"/>.</exception>
    /// <exception cref="IOException">Something already exists at <paramref name="to"/>.</exception>
    public Task<AssetRecord> MoveAsync(string from, string to, CancellationToken cancellationToken = default)
    {
        var source = ValidatePath(from);
        var target = ValidatePath(to);
        return RunAsync(() =>
        {
            MoveWithMeta(source, target);
            return Task.FromResult<IReadOnlyList<string>>([source, target]);
        }, target, cancellationToken);
    }

    /// <summary>Renames an asset or folder within its folder, with its .meta file.</summary>
    /// <exception cref="ArgumentException">The name is empty, contains a path separator or is not a valid file name.</exception>
    public Task<AssetRecord> RenameAsync(string path, string newName, CancellationToken cancellationToken = default)
    {
        ValidateName(newName);
        var source = ValidatePath(path);
        return MoveAsync(source, AssetPath.Combine(AssetPath.GetDirectory(source), newName), cancellationToken);
    }

    /// <summary>Copies an asset or folder next to the original as "name 1.ext", giving every copy a new guid and the same settings.</summary>
    public Task<AssetRecord> DuplicateAsync(string path, CancellationToken cancellationToken = default)
    {
        var source = ValidatePath(path);
        var target = source;
        return RunAsync(async () =>
        {
            var fullSource = ToFullPath(source);
            var isFolder = Directory.Exists(fullSource);
            if (!isFolder && !File.Exists(fullSource))
                throw new FileNotFoundException($"'{source}' does not exist.", fullSource);

            target = UniqueCopyPath(source, isFolder);
            await CopyWithNewGuidsAsync(source, target, isFolder, cancellationToken).ConfigureAwait(false);
            return [target];
        }, () => target, cancellationToken);
    }

    /// <summary>Deletes an asset or folder with its .meta file, moving them to the trash folder when there is one.</summary>
    /// <returns>Where the asset now is in the trash, for <see cref="RestoreAsync"/>; null when it was deleted permanently.</returns>
    public async Task<string?> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = ValidatePath(path);
        EnsureScanned();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Reconciliation reconciliation;
        string? trashed = null;
        try
        {
            var fullPath = ToFullPath(normalized);
            var isFolder = Directory.Exists(fullPath);
            if (!isFolder && !File.Exists(fullPath))
                throw new FileNotFoundException($"'{normalized}' does not exist.", fullPath);

            if (_options.TrashFolder is { } trash)
            {
                var batch = Path.Combine(trash, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
                var target = Path.Combine(batch, normalized.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                MoveEntry(fullPath, target, isFolder);
                if (File.Exists(fullPath + AssetMetaFile.Extension))
                    File.Move(fullPath + AssetMetaFile.Extension, target + AssetMetaFile.Extension);
                trashed = target;
            }
            else
            {
                if (isFolder)
                    Directory.Delete(fullPath, recursive: true);
                else
                    File.Delete(fullPath);
                File.Delete(fullPath + AssetMetaFile.Extension);
            }

            reconciliation = await Task.Run(() => ReconcileAsync(new HashSet<string>([normalized], AssetPath.Comparer), null, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        ScheduleCacheSave();
        Raise(reconciliation.Changes);
        return trashed;
    }

    /// <summary>Moves an asset that <see cref="DeleteAsync"/> put in the trash back to a path, with its .meta file and so its guid.</summary>
    /// <param name="trashedPath">The location <see cref="DeleteAsync"/> returned.</param>
    /// <exception cref="FileNotFoundException">Nothing is at <paramref name="trashedPath"/>.</exception>
    /// <exception cref="IOException">Something already exists at <paramref name="path"/>.</exception>
    public Task<AssetRecord> RestoreAsync(string trashedPath, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(trashedPath);
        var target = ValidatePath(path);
        return RunAsync(() =>
        {
            var isFolder = Directory.Exists(trashedPath);
            if (!isFolder && !File.Exists(trashedPath))
                throw new FileNotFoundException($"'{trashedPath}' is not in the trash.", trashedPath);
            var fullTarget = EnsureFreeTarget(target);
            MoveEntry(trashedPath, fullTarget, isFolder);
            if (File.Exists(trashedPath + AssetMetaFile.Extension))
                File.Move(trashedPath + AssetMetaFile.Extension, fullTarget + AssetMetaFile.Extension);
            return Task.FromResult<IReadOnlyList<string>>([target]);
        }, target, cancellationToken);
    }

    /// <summary>Creates a file with the given contents and its .meta file, such as a new scene.</summary>
    /// <exception cref="IOException">Something already exists at the path.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public Task<AssetRecord> CreateFileAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken = default)
    {
        var normalized = ValidatePath(path);
        return RunAsync(async () =>
        {
            var fullPath = EnsureFreeTarget(normalized);
            await AtomicFile.WriteAllBytesAsync(fullPath, contents, cancellationToken).ConfigureAwait(false);
            return [normalized];
        }, normalized, cancellationToken);
    }

    /// <summary>Replaces the contents of an existing asset file, keeping its .meta file and guid.</summary>
    /// <exception cref="FileNotFoundException">No file exists at the path.</exception>
    public Task<AssetRecord> WriteFileAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken = default)
    {
        var normalized = ValidatePath(path);
        return RunAsync(async () =>
        {
            var fullPath = ToFullPath(normalized);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"'{normalized}' does not exist.", fullPath);
            await AtomicFile.WriteAllBytesAsync(fullPath, contents, cancellationToken).ConfigureAwait(false);
            return [normalized];
        }, normalized, cancellationToken);
    }

    /// <summary>Copies a file or folder from outside the asset folder to a path inside it; every copied asset gets a new .meta file.</summary>
    /// <param name="sourcePath">The absolute path of a file or folder.</param>
    /// <exception cref="FileNotFoundException">Nothing exists at <paramref name="sourcePath"/>.</exception>
    /// <exception cref="IOException">Something already exists at <paramref name="path"/>.</exception>
    public Task<AssetRecord> ImportAsync(string sourcePath, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        var normalized = ValidatePath(path);
        return RunAsync(() =>
        {
            var isFolder = Directory.Exists(sourcePath);
            if (!isFolder && !File.Exists(sourcePath))
                throw new FileNotFoundException($"'{sourcePath}' does not exist.", sourcePath);
            var fullPath = EnsureFreeTarget(normalized);
            if (isFolder)
                CopyFolderWithoutMetas(sourcePath, fullPath);
            else
                File.Copy(sourcePath, fullPath);
            return Task.FromResult<IReadOnlyList<string>>([normalized]);
        }, normalized, cancellationToken);
    }

    private string EnsureFreeTarget(string path)
    {
        var fullPath = ToFullPath(path);
        if (File.Exists(fullPath) || Directory.Exists(fullPath) || File.Exists(fullPath + AssetMetaFile.Extension))
            throw new IOException($"'{path}' already exists.");
        if (!Directory.Exists(Path.GetDirectoryName(fullPath)))
            throw new DirectoryNotFoundException($"The folder '{AssetPath.GetDirectory(path)}' does not exist.");
        return fullPath;
    }

    private static void CopyFolderWithoutMetas(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (!AssetMetaFile.IsMetaPath(file))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var folder in Directory.EnumerateDirectories(source))
            CopyFolderWithoutMetas(folder, Path.Combine(target, Path.GetFileName(folder)));
    }

    /// <summary>Changes the .meta file of an asset, such as its labels or import settings; the guid cannot change.</summary>
    /// <exception cref="FileNotFoundException">The database has no asset at the path.</exception>
    public Task<AssetRecord> UpdateMetaAsync(string path, Func<AssetMeta, AssetMeta> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var normalized = ValidatePath(path);
        return RunAsync(async () =>
        {
            if (!TryGetAsset(normalized, out var record))
                throw new FileNotFoundException($"'{normalized}' is not an asset.");
            var meta = update(record.Meta) ?? throw new InvalidOperationException("The update returned no meta.");
            if (meta.Guid != record.Guid || meta.IsFolder != record.IsFolder)
                throw new InvalidOperationException("Updating a .meta file cannot change the asset's guid or whether it is a folder.");
            await AssetMetaFile.WriteAsync(ToFullPath(normalized) + AssetMetaFile.Extension, meta, cancellationToken).ConfigureAwait(false);
            return [normalized];
        }, normalized, cancellationToken);
    }

    /// <summary>Stores new import settings for an asset in its .meta file.</summary>
    public Task<AssetRecord> SetImportSettingsAsync<TSettings>(string path, TSettings settings, CancellationToken cancellationToken = default) where TSettings : class =>
        UpdateMetaAsync(path, meta => meta.WithSettings(settings), cancellationToken);

    /// <summary>Records that an asset was imported in its current state, so <see cref="AssetRecord.NeedsImport"/> becomes false.</summary>
    public async Task MarkImportedAsync(AssetGuid guid, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                if (_records.TryGetValue(guid, out var record) && record.ImportedStamp != record.ImportStamp)
                    _records[guid] = record with { ImportedStamp = record.ImportStamp };
            }
        }
        finally
        {
            _gate.Release();
        }

        ScheduleCacheSave();
    }

    /// <summary>Deletes .meta files whose assets no longer exist.</summary>
    /// <returns>The number of files deleted.</returns>
    public async Task<int> RemoveOrphanedMetasAsync(CancellationToken cancellationToken = default)
    {
        EnsureScanned();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string[] orphans;
            lock (_stateLock)
                orphans = [.. _orphanedMetas];

            var deleted = 0;
            foreach (var path in orphans)
            {
                var fullPath = ToFullPath(path);
                if (File.Exists(fullPath) || Directory.Exists(fullPath))
                    continue;
                File.Delete(fullPath + AssetMetaFile.Extension);
                deleted++;
                lock (_stateLock)
                    _orphanedMetas.Remove(path);
            }

            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<AssetRecord> RunAsync(Func<Task<IReadOnlyList<string>>> operation, string resultPath, CancellationToken cancellationToken) =>
        RunAsync(operation, () => resultPath, cancellationToken);

    /// <summary>Runs a file operation under the gate, then reconciles the paths it touched and reports the changes.</summary>
    private async Task<AssetRecord> RunAsync(Func<Task<IReadOnlyList<string>>> operation, Func<string> resultPath, CancellationToken cancellationToken)
    {
        EnsureScanned();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Reconciliation reconciliation;
        try
        {
            var touched = await operation().ConfigureAwait(false);
            reconciliation = await Task.Run(() => ReconcileAsync(touched.ToHashSet(AssetPath.Comparer), null, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        ScheduleCacheSave();
        Raise(reconciliation.Changes);
        return TryGetAsset(resultPath(), out var record)
            ? record
            : throw new IOException($"'{resultPath()}' could not be read after the operation.");
    }

    private void MoveWithMeta(string source, string target)
    {
        var fullSource = ToFullPath(source);
        var fullTarget = ToFullPath(target);
        var isFolder = Directory.Exists(fullSource);
        if (!isFolder && !File.Exists(fullSource))
            throw new FileNotFoundException($"'{source}' does not exist.", fullSource);
        if (string.Equals(source, target, StringComparison.Ordinal))
            return;

        var caseOnly = AssetPath.Comparer.Equals(source, target);
        if (!caseOnly && (File.Exists(fullTarget) || Directory.Exists(fullTarget)))
            throw new IOException($"'{target}' already exists.");
        if (!caseOnly && File.Exists(fullTarget + AssetMetaFile.Extension))
            throw new IOException($"'{target}{AssetMetaFile.Extension}' already exists.");
        if (isFolder && AssetPath.IsWithin(target, source))
            throw new IOException($"'{source}' cannot be moved into itself.");
        if (!Directory.Exists(ToFullPath(AssetPath.GetDirectory(target))))
            throw new DirectoryNotFoundException($"The folder '{AssetPath.GetDirectory(target)}' does not exist.");

        MoveEntry(fullSource, fullTarget, isFolder);
        try
        {
            if (File.Exists(fullSource + AssetMetaFile.Extension))
                MoveEntry(fullSource + AssetMetaFile.Extension, fullTarget + AssetMetaFile.Extension, isFolder: false);
        }
        catch
        {
            MoveEntry(fullTarget, fullSource, isFolder);
            throw;
        }
    }

    /// <summary>Moves a file or folder, going through a temporary name for renames that only change case.</summary>
    private static void MoveEntry(string source, string target, bool isFolder)
    {
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase) && !string.Equals(source, target, StringComparison.Ordinal))
        {
            var temporary = Path.Combine(Path.GetDirectoryName(source)!, $".{Path.GetFileName(source)}.{Guid.NewGuid():N}.rename");
            MoveEntry(source, temporary, isFolder);
            MoveEntry(temporary, target, isFolder);
            return;
        }

        if (isFolder)
            Directory.Move(source, target);
        else
            File.Move(source, target);
    }

    private string UniqueCopyPath(string source, bool isFolder)
    {
        var folder = AssetPath.GetDirectory(source);
        var name = AssetPath.GetFileName(source);
        var extension = isFolder ? string.Empty : Path.GetExtension(name);
        var stem = name[..^extension.Length];
        var trailingNumber = stem.LastIndexOf(' ');
        if (trailingNumber > 0 && int.TryParse(stem[(trailingNumber + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out _))
            stem = stem[..trailingNumber];

        for (var index = 1; ; index++)
        {
            var candidate = AssetPath.Combine(folder, $"{stem} {index.ToString(CultureInfo.InvariantCulture)}{extension}");
            var fullPath = ToFullPath(candidate);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath) && !File.Exists(fullPath + AssetMetaFile.Extension))
                return candidate;
        }
    }

    private async Task CopyWithNewGuidsAsync(string source, string target, bool isFolder, CancellationToken cancellationToken)
    {
        var fullSource = ToFullPath(source);
        var fullTarget = ToFullPath(target);
        if (isFolder)
        {
            Directory.CreateDirectory(fullTarget);
            foreach (var child in new DirectoryInfo(fullSource).EnumerateFileSystemInfos())
            {
                var childSource = $"{source}/{child.Name}";
                if (IsIgnoredPath(childSource))
                    continue;
                await CopyWithNewGuidsAsync(childSource, $"{target}/{child.Name}", child is DirectoryInfo, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            File.Copy(fullSource, fullTarget);
        }

        AssetMeta? meta;
        try
        {
            meta = await AssetMetaFile.TryReadAsync(fullSource + AssetMetaFile.Extension, cancellationToken).ConfigureAwait(false);
        }
        catch (AssetException)
        {
            meta = null;
        }

        var copy = meta is null
            ? isFolder ? AssetMeta.CreateFolder(AssetGuid.NewGuid()) : new AssetMeta(AssetGuid.NewGuid())
            : meta with { Guid = AssetGuid.NewGuid() };
        await AssetMetaFile.WriteAsync(fullTarget + AssetMetaFile.Extension, copy, cancellationToken).ConfigureAwait(false);
    }

    private string ValidatePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var normalized = AssetPath.Normalize(path);
        if (normalized.Length == 0)
            throw new ArgumentException("The asset root itself cannot be changed.", nameof(path));
        if (IsIgnoredPath(normalized))
            throw new ArgumentException($"'{normalized}' is not an asset path: hidden, temporary and .meta files are not assets.", nameof(path));
        foreach (var segment in normalized.Split('/'))
            ValidateName(segment);
        return normalized;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.AsSpan().ContainsAny('/', '\\') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || name is "." or ".." || name != name.Trim())
            throw new ArgumentException($"'{name}' is not a valid file name.", nameof(name));
        if (name[0] == '.' || AssetMetaFile.IsMetaPath(name))
            throw new ArgumentException($"'{name}' would be ignored: names starting with a dot or ending in .meta are not assets.", nameof(name));
    }
}
