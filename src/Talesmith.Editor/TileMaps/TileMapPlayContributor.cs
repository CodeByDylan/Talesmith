using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Packs;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.TileMaps;

/// <summary>Plays maps with unsaved edits: writes each into memory as a .hexy package when play starts, and the play session reads those
/// instead of the files.</summary>
/// <remarks>The play session imports its own copy, so it never shares the edited map and its changes never reach the editor.</remarks>
internal sealed class TileMapPlayContributor(TileMapDocuments maps) : IPlaySessionContributor
{
    private Dictionary<string, byte[]>? _snapshots;

    public async ValueTask<string?> PrepareAsync(CancellationToken cancellationToken)
    {
        _snapshots = null;
        var writes = maps.Maps.Where(map => !string.IsNullOrEmpty(map.Path) && maps.IsDirty(map))
            .Select(map => (map.Path, Bytes: SnapshotAsync(map, cancellationToken)))
            .ToList();
        if (writes.Count == 0)
            return null;
        await Task.WhenAll(writes.Select(write => write.Bytes));
        _snapshots = writes.ToDictionary(write => AssetPath.Normalize(write.Path), write => write.Bytes.Result, AssetPath.Comparer);
        return null;
    }

    public GameSessionRequest Contribute(GameSessionRequest request)
    {
        if (Interlocked.Exchange(ref _snapshots, null) is not { } snapshots)
            return request;
        var configure = request.Configure;
        return request with
        {
            Configure = builder =>
            {
                configure?.Invoke(builder);
                builder.Services.AddSingleton<IAssetSource>(new OverlayAssetSource(snapshots, PackAssetSource.OpenFolder(builder.AssetRoot)));
            }
        };
    }

    /// <summary>Captures the map on the calling thread, which must be the edit game's; packaging continues on the thread pool.</summary>
    private static async Task<byte[]> SnapshotAsync(TileMap map, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await HexyMapWriter.WriteAsync(map, buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>Serves files from memory in place of the same files of another source.</summary>
    private sealed class OverlayAssetSource(IReadOnlyDictionary<string, byte[]> files, IAssetSource fallback) : IAssetSource
    {
        public string Description => $"unsaved editor files over {fallback.Description}";

        public bool Exists(string path) => files.ContainsKey(AssetPath.Normalize(path)) || fallback.Exists(path);

        public Stream OpenRead(string path) =>
            files.TryGetValue(AssetPath.Normalize(path), out var data) ? new MemoryStream(data, writable: false) : fallback.OpenRead(path);

        public IEnumerable<string> List(string folder, string pattern = "*", bool recursive = false) => fallback.List(folder, pattern, recursive);

        public string? GetFullPath(string path) => files.ContainsKey(AssetPath.Normalize(path)) ? null : fallback.GetFullPath(path);
    }
}
