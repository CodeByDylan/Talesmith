using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Database.Dependencies;

namespace Talesmith.Assets.Database;

public sealed partial class AssetDatabase
{
    /// <summary>Reads the whole asset folder and brings the database, the .meta files and the catalog up to date.</summary>
    public async Task<AssetScanResult> ScanAsync(IProgress<AssetScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Reconciliation reconciliation;
        try
        {
            if (!IsScanned)
                _cache = await AssetDatabaseCache.LoadAsync(_options.CacheFolder, cancellationToken).ConfigureAwait(false);
            reconciliation = await Task.Run(() => ReconcileAsync(null, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            lock (_stateLock)
                _scanned = true;
        }
        finally
        {
            _gate.Release();
        }

        ScheduleCacheSave();
        Raise(reconciliation.Changes);
        LogScanned(reconciliation.Result.AssetCount, reconciliation.Result.Duration.TotalMilliseconds);
        return reconciliation.Result;
    }

    /// <summary>Re-reads specific paths, such as files an external tool wrote, and reports what changed.</summary>
    public async Task<IReadOnlyList<AssetChange>> RefreshAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        EnsureScanned();
        var dirty = paths.Select(AssetPath.Normalize).ToHashSet(AssetPath.Comparer);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Reconciliation reconciliation;
        try
        {
            reconciliation = await Task.Run(() => ReconcileAsync(dirty, null, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        ScheduleCacheSave();
        Raise(reconciliation.Changes);
        return reconciliation.Changes;
    }

    /// <summary>Compares the files at the dirty paths, or every file when null, with the database and applies the differences.</summary>
    /// <remarks>Runs under the gate, so it is the only code changing records.</remarks>
    private async Task<Reconciliation> ReconcileAsync(IReadOnlySet<string>? dirty, IProgress<AssetScanProgress>? progress, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var entries = new List<(string Path, bool IsFolder)>();
        var metaFiles = new HashSet<string>(AssetPath.Comparer);
        var candidates = new HashSet<string>(AssetPath.Comparer);
        Dictionary<AssetGuid, AssetRecord> previous;
        lock (_stateLock)
            previous = new Dictionary<AssetGuid, AssetRecord>(_records);
        var previousByPath = previous.Values.ToDictionary(record => record.Path, AssetPath.Comparer);

        if (dirty is null)
        {
            Walk(string.Empty, entries, metaFiles);
            candidates.UnionWith(previousByPath.Keys);
        }
        else
        {
            foreach (var requested in dirty)
            {
                var path = OnDiskPath(requested);
                if (path.Length == 0)
                {
                    Walk(string.Empty, entries, metaFiles);
                    candidates.UnionWith(previousByPath.Keys);
                    continue;
                }

                if (IsIgnoredPath(path))
                    continue;
                candidates.Add(path);
                candidates.UnionWith(previousByPath.Keys.Where(recorded => AssetPath.IsWithin(recorded, path)));
                var fullPath = ToFullPath(path);
                if (File.Exists(fullPath + AssetMetaFile.Extension))
                    metaFiles.Add(path);
                if (Directory.Exists(fullPath))
                {
                    entries.Add((path, true));
                    Walk(path, entries, metaFiles);
                }
                else if (File.Exists(fullPath))
                {
                    entries.Add((path, false));
                }
            }
        }

        entries = [.. entries.DistinctBy(entry => entry.Path, AssetPath.Comparer)];
        candidates.UnionWith(entries.Select(entry => entry.Path));

        var observations = await ObserveAsync(entries, previous, previousByPath, progress, cancellationToken).ConfigureAwait(false);
        var observedPaths = observations.Select(observation => observation.Path).ToHashSet(AssetPath.Comparer);
        var gone = candidates
            .Where(path => !observedPaths.Contains(path) && previousByPath.ContainsKey(path))
            .Select(path => previousByPath[path])
            .ToList();

        var repairs = new List<DuplicateGuidRepair>();
        var invalid = new List<InvalidMeta>();
        AssignGuids(observations, previous, previousByPath, candidates, gone, repairs);
        var metasCreated = await WriteMetasAsync(observations, invalid, cancellationToken).ConfigureAwait(false);

        var changes = new List<AssetChange>();
        var updated = new Dictionary<AssetGuid, AssetRecord>();
        foreach (var observation in observations.OrderBy(observation => observation.Path, StringComparer.Ordinal))
        {
            var record = CreateRecord(observation, previous);
            updated[record.Guid] = record;
            var before = observation.Repair is null ? previous.GetValueOrDefault(record.Guid) : null;
            if (before is null)
            {
                changes.Add(new AssetChange(AssetChangeKind.Added, record.Guid, record.Path, record.Kind) { ReplacedGuid = observation.Repair?.Guid ?? AssetGuid.Empty });
                continue;
            }

            var reasons = AssetChangeReasons.None;
            if (before.ContentHash != record.ContentHash)
                reasons |= AssetChangeReasons.Content;
            if (!(before.Meta with { ImporterVersion = record.Meta.ImporterVersion }).ContentEquals(record.Meta))
                reasons |= AssetChangeReasons.Settings;
            if (observation.ImporterUpgraded)
                reasons |= AssetChangeReasons.ImporterVersion;

            if (!string.Equals(before.Path, record.Path, StringComparison.Ordinal))
                changes.Add(new AssetChange(AssetChangeKind.Moved, record.Guid, record.Path, record.Kind) { OldPath = before.Path, Reasons = reasons });
            else if (reasons != AssetChangeReasons.None)
                changes.Add(new AssetChange(AssetChangeKind.Changed, record.Guid, record.Path, record.Kind) { Reasons = reasons });
        }

        var removed = new List<AssetRecord>();
        foreach (var record in gone)
        {
            if (!updated.ContainsKey(record.Guid))
                removed.Add(record);
        }

        foreach (var observation in observations)
        {
            if (previousByPath.TryGetValue(observation.Path, out var displaced) && displaced.Guid != observation.Meta!.Guid
                && !updated.ContainsKey(displaced.Guid) && !removed.Contains(displaced))
                removed.Add(displaced);
        }

        foreach (var record in removed)
            changes.Add(new AssetChange(AssetChangeKind.Deleted, record.Guid, record.Path, record.Kind));

        Apply(updated.Values, removed, previous, dirty is null, metaFiles, candidates, observedPaths, repairs, invalid, observations);
        var result = new AssetScanResult(updated.Count, metasCreated, repairs, invalid, stopwatch.Elapsed);
        return new Reconciliation(changes, result);
    }

    /// <summary>The path as the file system spells it.</summary>
    /// <remarks>
    /// Where paths ignore case, a dirty path can differ from the name on disk in case only, such as the old name after a rename that only
    /// changed the case. Records keep the spelling on disk.
    /// </remarks>
    private string OnDiskPath(string path)
    {
        if (AssetPath.Comparison == StringComparison.Ordinal || path.Length == 0)
            return path;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, MatchCasing = MatchCasing.CaseInsensitive };
        var segments = path.Split('/');
        var folder = new DirectoryInfo(RootFolder);
        for (var i = 0; i < segments.Length; i++)
        {
            FileSystemInfo? entry;
            try
            {
                entry = folder.EnumerateFileSystemInfos(segments[i], options).FirstOrDefault(e => string.Equals(e.Name, segments[i], AssetPath.Comparison));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return path;
            }

            if (entry is null)
                return path;
            segments[i] = entry.Name;
            if (entry is DirectoryInfo child)
                folder = child;
            else if (i < segments.Length - 1)
                return path;
        }

        return string.Join('/', segments);
    }

    private void Walk(string folder, List<(string Path, bool IsFolder)> entries, HashSet<string> metaFiles)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0, RecurseSubdirectories = false };
        List<FileSystemInfo> children;
        try
        {
            children = [.. new DirectoryInfo(ToFullPath(folder)).EnumerateFileSystemInfos("*", options)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var child in children)
        {
            var name = child.Name;
            var path = folder.Length == 0 ? name : $"{folder}/{name}";
            if (AssetMetaFile.IsMetaPath(name) && name[0] != '.')
            {
                metaFiles.Add(AssetMetaFile.GetAssetPath(path));
                continue;
            }

            if (IsIgnoredPath(path))
                continue;

            var isFolder = child is DirectoryInfo;
            entries.Add((path, isFolder));
            if (isFolder && !child.Attributes.HasFlag(FileAttributes.ReparsePoint))
                Walk(path, entries, metaFiles);
        }
    }

    private async Task<List<Observation>> ObserveAsync(
        List<(string Path, bool IsFolder)> entries,
        Dictionary<AssetGuid, AssetRecord> previous,
        Dictionary<string, AssetRecord> previousByPath,
        IProgress<AssetScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var observations = new Observation?[entries.Count];
        var processed = 0;
        ReportProgress(new AssetScanProgress(0, entries.Count, null), progress);
        var options = new ParallelOptions { MaxDegreeOfParallelism = _options.MaxParallelism, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Count), options, async (index, token) =>
        {
            var (path, isFolder) = entries[index];
            observations[index] = await ObserveAsync(path, isFolder, previous, previousByPath, token).ConfigureAwait(false);
            var done = Interlocked.Increment(ref processed);
            if (done % 64 == 0 || done == entries.Count)
                ReportProgress(new AssetScanProgress(done, entries.Count, path), progress);
        }).ConfigureAwait(false);
        ReportProgress(new AssetScanProgress(entries.Count, entries.Count, null), progress);
        return [.. observations.OfType<Observation>()];
    }

    private async Task<Observation?> ObserveAsync(
        string path,
        bool isFolder,
        Dictionary<AssetGuid, AssetRecord> previous,
        Dictionary<string, AssetRecord> previousByPath,
        CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(path);
        var observation = new Observation(path, isFolder);
        try
        {
            observation.Meta = await AssetMetaFile.TryReadAsync(fullPath + AssetMetaFile.Extension, cancellationToken).ConfigureAwait(false);
        }
        catch (AssetException ex)
        {
            observation.InvalidMetaError = ex.Message;
        }
        catch (IOException ex)
        {
            LogUnreadable(path, ex);
            return null;
        }

        if (observation.Meta is not null && observation.Meta.IsFolder != isFolder)
        {
            observation.Meta = observation.Meta with { IsFolder = isFolder, Importer = isFolder ? null : observation.Meta.Importer };
            observation.NeedsWrite = true;
        }

        try
        {
            if (isFolder)
            {
                var info = new DirectoryInfo(fullPath);
                observation.CreationTimeUtc = info.CreationTimeUtc;
                observation.LastWriteTimeUtc = info.LastWriteTimeUtc;
                return info.Exists ? observation : null;
            }

            var file = new FileInfo(fullPath);
            if (!file.Exists)
                return null;
            observation.Size = file.Length;
            observation.LastWriteTimeUtc = file.LastWriteTimeUtc;
            observation.CreationTimeUtc = file.CreationTimeUtc;

            var known = previousByPath.GetValueOrDefault(path) is { } atPath ? Known.From(atPath) : null;
            if (known is null && observation.Meta is not null && previous.TryGetValue(observation.Meta.Guid, out var byGuid))
                known = Known.From(byGuid);
            if (known is null && _cache.TryGetValue(path, out var cached))
                known = new Known(cached.Hash, cached.Size, cached.LastWriteTimeUtc, cached.References);

            if (known is { Hash: not null } && known.Size == observation.Size && known.LastWriteTimeUtc == observation.LastWriteTimeUtc)
            {
                observation.Hash = known.Hash;
                observation.References = known.References;
                return observation;
            }

            observation.Hash = await HashFileAsync(fullPath, cancellationToken).ConfigureAwait(false);
            observation.References = await ExtractReferencesAsync(path, fullPath, cancellationToken).ConfigureAwait(false);
            return observation;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnreadable(path, ex);
            return null;
        }
    }

    private async Task<IReadOnlyList<AssetReference>> ExtractReferencesAsync(string path, string fullPath, CancellationToken cancellationToken)
    {
        if (FindExtractor(path) is not { } extractor)
            return [];

        try
        {
            return await extractor.ExtractAsync(new AssetDependencyContext(path, fullPath), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogExtractionFailed(path, ex);
            return [];
        }
    }

    private static async Task<string> HashFileAsync(string fullPath, CancellationToken cancellationToken)
    {
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Decides the guid of every observation: keeps existing guids, pairs moves and gives copies and new files new guids.</summary>
    private void AssignGuids(
        List<Observation> observations,
        Dictionary<AssetGuid, AssetRecord> previous,
        Dictionary<string, AssetRecord> previousByPath,
        HashSet<string> candidates,
        List<AssetRecord> gone,
        List<DuplicateGuidRepair> repairs)
    {
        var cachedPaths = _cache.Values.GroupBy(cached => cached.Guid).ToDictionary(group => group.Key, group => group.First().Path);
        foreach (var group in observations.Where(o => o.Meta is not null).GroupBy(o => o.Meta!.Guid).OrderBy(group => group.Min(o => o.Path), StringComparer.Ordinal))
        {
            var members = group.ToList();
            Observation? winner;
            string keptPath;
            if (previous.TryGetValue(group.Key, out var existing))
            {
                winner = members.FirstOrDefault(o => AssetPath.Comparer.Equals(o.Path, existing.Path));
                var stillThere = !candidates.Contains(existing.Path) && (File.Exists(ToFullPath(existing.Path)) || Directory.Exists(ToFullPath(existing.Path)));
                if (winner is null && !stillThere)
                    winner = ChooseOriginal(members, existing.Path);
                keptPath = winner?.Path ?? existing.Path;
            }
            else
            {
                winner = ChooseOriginal(members, cachedPaths.GetValueOrDefault(group.Key));
                keptPath = winner.Path;
            }

            foreach (var member in members)
            {
                if (member == winner)
                    continue;

                var replacement = AssetGuid.NewGuid();
                member.Repair = new DuplicateGuidRepair(group.Key, keptPath, member.Path, replacement);
                member.Meta = member.Meta! with { Guid = replacement };
                member.NeedsWrite = true;
                repairs.Add(member.Repair);
                LogDuplicateGuid(member.Path, keptPath, group.Key);
            }
        }

        var claimed = observations.Where(o => o.Meta is not null).Select(o => o.Meta!.Guid).ToHashSet();
        var unclaimed = gone.Where(record => !claimed.Contains(record.Guid)).ToList();
        foreach (var observation in observations.Where(o => o.Meta is null).OrderBy(o => o.Path, StringComparer.Ordinal))
        {
            if (KnownMeta(observation, previousByPath) is { } known && claimed.Add(known.Guid))
            {
                observation.Meta = known;
                observation.NeedsWrite = true;
                continue;
            }

            var moved = observation.IsFolder || observation.Hash is null
                ? null
                : unclaimed.Where(record => !record.IsFolder && record.ContentHash == observation.Hash).Take(2).ToList() is [var single] ? single : null;
            if (moved is not null)
            {
                unclaimed.Remove(moved);
                observation.Meta = moved.Meta;
                observation.MovedMetaFrom = moved.Path;
            }
            else
            {
                observation.Meta = observation.IsFolder ? AssetMeta.CreateFolder(AssetGuid.NewGuid()) : new AssetMeta(AssetGuid.NewGuid());
                observation.Created = true;
            }

            observation.NeedsWrite = true;
        }

        foreach (var observation in observations)
        {
            if (observation.IsFolder || FindImporter(observation.Path) is not { } importer)
                continue;

            var meta = observation.Meta!;
            if (meta.Importer is null)
            {
                observation.Meta = meta with { Importer = importer.Id, ImporterVersion = importer.Version };
                observation.NeedsWrite = true;
            }
            else if (meta.Importer == importer.Id && meta.ImporterVersion < importer.Version)
            {
                observation.Meta = meta with { ImporterVersion = importer.Version };
                observation.ImporterUpgraded = !observation.Created;
                observation.NeedsWrite = true;
            }
        }
    }

    /// <summary>The meta an asset had before its .meta file went missing or became unreadable, so it keeps its guid.</summary>
    private AssetMeta? KnownMeta(Observation observation, Dictionary<string, AssetRecord> previousByPath)
    {
        if (previousByPath.TryGetValue(observation.Path, out var record) && record.IsFolder == observation.IsFolder)
            return record.Meta;
        if (_cache.TryGetValue(observation.Path, out var cached) && cached.Hash == observation.Hash && !Catalog.TryGetPath(cached.Guid, out _))
            return observation.IsFolder ? AssetMeta.CreateFolder(cached.Guid) : new AssetMeta(cached.Guid);
        return null;
    }

    private static Observation ChooseOriginal(List<Observation> members, string? preferredPath) =>
        members
            .OrderBy(o => preferredPath is not null && AssetPath.Comparer.Equals(o.Path, preferredPath) ? 0 : 1)
            .ThenBy(o => o.CreationTimeUtc)
            .ThenBy(o => o.Path, StringComparer.Ordinal)
            .First();

    private async Task<int> WriteMetasAsync(List<Observation> observations, List<InvalidMeta> invalid, CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var observation in observations)
        {
            if (!observation.NeedsWrite)
                continue;

            var metaFile = ToFullPath(observation.Path) + AssetMetaFile.Extension;
            if (observation.InvalidMetaError is { } error)
            {
                var backup = Path.Combine(Path.GetDirectoryName(metaFile)!, $".{Path.GetFileName(metaFile)}.invalid");
                File.Copy(metaFile, backup, overwrite: true);
                invalid.Add(new InvalidMeta(observation.Path, error));
                LogInvalidMeta(observation.Path, error);
            }

            await AssetMetaFile.WriteAsync(metaFile, observation.Meta!, cancellationToken).ConfigureAwait(false);
            if (observation.MovedMetaFrom is { } oldPath)
            {
                var oldMeta = ToFullPath(oldPath) + AssetMetaFile.Extension;
                if (File.Exists(oldMeta) && !File.Exists(ToFullPath(oldPath)))
                    File.Delete(oldMeta);
            }

            if (observation.Created)
                created++;
        }

        return created;
    }

    private AssetRecord CreateRecord(Observation observation, Dictionary<AssetGuid, AssetRecord> previous)
    {
        var meta = observation.Meta!;
        var before = observation.Repair is null ? previous.GetValueOrDefault(meta.Guid) : null;
        var imported = before?.ImportedStamp;
        if (before is null && _cache.TryGetValue(observation.Path, out var cached) && cached.Guid == meta.Guid)
            imported = cached.ImportedStamp;

        return new AssetRecord
        {
            Guid = meta.Guid,
            Path = observation.Path,
            Kind = observation.IsFolder ? AssetKind.Folder : Kinds.Classify(observation.Path),
            Meta = meta,
            ContentHash = observation.Hash,
            Size = observation.Size,
            LastWriteTimeUtc = observation.LastWriteTimeUtc,
            References = observation.References,
            ImportStamp = ComputeImportStamp(observation.Hash, meta),
            ImportedStamp = imported
        };
    }

    private static string? ComputeImportStamp(string? hash, AssetMeta meta)
    {
        if (hash is null || meta.Importer is null)
            return null;
        var settings = meta.Settings?.GetRawText() ?? string.Empty;
        var text = $"{hash}|{meta.Importer}|{meta.ImporterVersion}|{settings}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    private void Apply(
        IEnumerable<AssetRecord> updated,
        List<AssetRecord> removed,
        Dictionary<AssetGuid, AssetRecord> previous,
        bool full,
        HashSet<string> metaFiles,
        HashSet<string> candidates,
        HashSet<string> observedPaths,
        List<DuplicateGuidRepair> repairs,
        List<InvalidMeta> invalid,
        List<Observation> observations)
    {
        var records = updated.ToList();
        lock (_stateLock)
        {
            foreach (var record in removed)
            {
                _records.Remove(record.Guid);
                if (_byPath.TryGetValue(record.Path, out var guid) && guid == record.Guid)
                    _byPath.Remove(record.Path);
            }

            foreach (var record in records)
            {
                if (previous.TryGetValue(record.Guid, out var old) && _byPath.TryGetValue(old.Path, out var guid) && guid == record.Guid)
                    _byPath.Remove(old.Path);
            }

            foreach (var record in records)
            {
                _records[record.Guid] = record;
                _byPath[record.Path] = record.Guid;
            }

            if (full)
                _orphanedMetas.Clear();
            foreach (var path in full ? metaFiles : candidates)
            {
                if (metaFiles.Contains(path) && !observedPaths.Contains(path) && !File.Exists(ToFullPath(path)) && !Directory.Exists(ToFullPath(path)))
                    _orphanedMetas.Add(path);
                else
                    _orphanedMetas.Remove(path);
            }

            foreach (var observation in observations)
            {
                if (observation.InvalidMetaError is null)
                    _invalidMetas.Remove(observation.Path);
            }

            foreach (var meta in invalid)
                _invalidMetas[meta.Path] = meta;
            _repairs.AddRange(repairs);
            _graph = null;
        }

        using (Catalog.BeginUpdate())
        {
            foreach (var record in removed)
                Catalog.Remove(record.Path);
            foreach (var record in records)
            {
                if (previous.TryGetValue(record.Guid, out var old) && !string.Equals(old.Path, record.Path, StringComparison.Ordinal))
                    Catalog.Remove(old.Path);
            }

            foreach (var record in records)
                Catalog.Set(record.Path, record.Meta);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scanned {Count} assets in {Milliseconds:0} ms")]
    private partial void LogScanned(int count, double milliseconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} could not be read and was skipped")]
    private partial void LogUnreadable(string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Finding the references of {Path} failed")]
    private partial void LogExtractionFailed(string path, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} had the guid {Guid} of {KeptPath}, so it was given a new guid")]
    private partial void LogDuplicateGuid(string path, string keptPath, AssetGuid guid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The .meta file of {Path} could not be read and was replaced: {Error}")]
    private partial void LogInvalidMeta(string path, string error);

    private sealed record Reconciliation(List<AssetChange> Changes, AssetScanResult Result);

    private sealed record Known(string? Hash, long Size, DateTime LastWriteTimeUtc, IReadOnlyList<AssetReference> References)
    {
        public static Known From(AssetRecord record) => new(record.ContentHash, record.Size, record.LastWriteTimeUtc, record.References);
    }

    private sealed class Observation(string path, bool isFolder)
    {
        public string Path { get; } = path;

        public bool IsFolder { get; } = isFolder;

        public AssetMeta? Meta { get; set; }

        public string? InvalidMetaError { get; set; }

        public string? Hash { get; set; }

        public long Size { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }

        public DateTime CreationTimeUtc { get; set; }

        public IReadOnlyList<AssetReference> References { get; set; } = [];

        public bool NeedsWrite { get; set; }

        public bool Created { get; set; }

        public bool ImporterUpgraded { get; set; }

        public DuplicateGuidRepair? Repair { get; set; }

        /// <summary>The path of a deleted asset whose .meta file this new file took over, because their contents match.</summary>
        public string? MovedMetaFrom { get; set; }
    }
}
