using System.Formats.Tar;
using System.IO.Compression;
using Talesmith.Assets;

namespace Talesmith.Build.Packaging;

/// <summary>Packs a build folder into the archive players download: a <c>.zip</c> for Windows and a <c>.tar.gz</c> for Linux.</summary>
/// <remarks>The <c>.tar.gz</c> keeps the executable flag of the player, so the game starts after unpacking.</remarks>
internal static class BuildArchive
{
    private const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead
                                            | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode Regular = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static string Extension(BuildTarget target) => target.Platform == BuildPlatform.Linux ? ".tar.gz" : ".zip";

    /// <summary>Writes the archive; entries are stored under the folder's own name.</summary>
    /// <param name="isExecutable">Whether a file, by path relative to the folder, gets the executable flag in a <c>.tar.gz</c>.</param>
    public static async Task WriteAsync(string folder, string archive, BuildTarget target, Func<string, bool> isExecutable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var root = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        await AtomicFile.WriteAsync(archive, (output, token) => target.Platform == BuildPlatform.Linux
            ? WriteTarGzAsync(output, folder, root, files, isExecutable, token)
            : WriteZipAsync(output, folder, root, files, token), cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteTarGzAsync(Stream output, string folder, string root, string[] files, Func<string, bool> isExecutable, CancellationToken cancellationToken)
    {
        await using var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true);
        await using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            await using var content = File.OpenRead(file);
            var entry = new PaxTarEntry(TarEntryType.RegularFile, $"{root}/{relative}")
            {
                Mode = isExecutable(relative) ? Executable : Regular,
                ModificationTime = File.GetLastWriteTimeUtc(file),
                DataStream = content
            };
            await tar.WriteEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteZipAsync(Stream output, string folder, string root, string[] files, CancellationToken cancellationToken)
    {
        await using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            var entry = zip.CreateEntry($"{root}/{relative}", CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(file);
            await using var target = await entry.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var content = File.OpenRead(file);
            await content.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }
    }
}
