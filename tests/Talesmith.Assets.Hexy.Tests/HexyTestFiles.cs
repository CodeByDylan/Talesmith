using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Hexy.Tests;

/// <summary>Loads and writes maps in a temporary asset folder, and finds the sample maps in the repository.</summary>
internal sealed class HexyTestFiles : IDisposable
{
    public HexyTestFiles() => Folder = Directory.CreateTempSubdirectory("talesmith-hexy-tests").FullName;

    public string Folder { get; }

    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Talesmith.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException("The repository root was not found.");
        }
    }

    /// <summary>Every sample map, as its assets folder and its path within it.</summary>
    public static TheoryData<string, string> SampleMaps
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "samples"), "*.hexy", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var assets = file[..file.LastIndexOf($"{Path.DirectorySeparatorChar}assets{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
                var root = Path.Combine(assets, "assets");
                data.Add(root, Path.GetRelativePath(root, file).Replace('\\', '/'));
            }

            return data;
        }
    }

    public static Task<TileMap> LoadAsync(string folder, string path) =>
        new AssetManager(new FileSystemAssetSource(folder), [new HexyMapImporter()], NullLogger<AssetManager>.Instance).LoadAsync<TileMap>(path);

    public Task<TileMap> LoadAsync(string path) => LoadAsync(Folder, path);

    public async Task<TileMap> LoadJsonAsync(string json)
    {
        var name = $"map-{Guid.NewGuid():N}.hexy";
        await File.WriteAllTextAsync(Path.Combine(Folder, name), json);
        return await LoadAsync(name);
    }

    /// <summary>Saves a map into the folder and loads it again.</summary>
    public async Task<TileMap> RoundTripAsync(TileMap map)
    {
        var name = $"saved-{Guid.NewGuid():N}.hexy";
        await HexyMapWriter.SaveAsync(map, Path.Combine(Folder, name));
        return await LoadAsync(name);
    }

    public static async Task<byte[]> WriteAsync(TileMap map)
    {
        using var stream = new MemoryStream();
        await HexyMapWriter.WriteAsync(map, stream);
        return stream.ToArray();
    }

    public static byte[] ReadEntry(byte[] package, string entry)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using var stream = (archive.GetEntry(entry) ?? throw new InvalidOperationException($"No {entry}")).Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    public static IReadOnlyList<string> Entries(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return archive.Entries.Select(e => e.FullName).ToArray();
    }

    public void Dispose() => Directory.Delete(Folder, recursive: true);
}
