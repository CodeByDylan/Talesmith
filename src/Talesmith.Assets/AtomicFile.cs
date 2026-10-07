using System.Text;

namespace Talesmith.Assets;

/// <summary>Writes files so that readers see either the old or the new contents, never a partial file.</summary>
/// <remarks>
/// The contents go to a hidden temporary file next to the target, which then replaces the target; if the process dies during the
/// replace, <see cref="Recover"/> restores the previous contents. Temporary and backup files start with a dot, so asset scans ignore them.
/// The folder is created when needed.
/// </remarks>
public static class AtomicFile
{
    private const int BufferSize = 1 << 16;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the file with what <paramref name="write"/> writes to the stream it is given.</summary>
    public static async Task WriteAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var (fullPath, temporary) = Prepare(path);
        try
        {
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                await write(stream, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            Replace(temporary, fullPath);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    /// <inheritdoc cref="WriteAsync(string, Func{Stream, CancellationToken, Task}, CancellationToken)"/>
    public static void Write(string path, Action<Stream> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var (fullPath, temporary) = Prepare(path);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            Replace(temporary, fullPath);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public static Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> contents, CancellationToken cancellationToken = default) =>
        WriteAsync(path, (stream, token) => stream.WriteAsync(contents, token).AsTask(), cancellationToken);

    /// <summary>Writes text as UTF-8 without a byte order mark.</summary>
    public static Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken = default) =>
        WriteAllBytesAsync(path, Utf8.GetBytes(contents), cancellationToken);

    /// <summary>Writes text as UTF-8 without a byte order mark.</summary>
    public static void WriteAllText(string path, string contents)
    {
        var bytes = Utf8.GetBytes(contents);
        Write(path, stream => stream.Write(bytes));
    }

    /// <summary>Restores a file from its backup when a write was interrupted after the old file was moved aside.</summary>
    /// <returns>Whether the file was restored.</returns>
    public static bool Recover(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var backup = GetBackupPath(fullPath);
        if (File.Exists(fullPath) || !File.Exists(backup))
            return false;

        File.Move(backup, fullPath);
        return true;
    }

    private static (string FullPath, string Temporary) Prepare(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new ArgumentException($"'{path}' has no folder.", nameof(path));
        Directory.CreateDirectory(directory);
        return (fullPath, Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp"));
    }

    private static void Replace(string temporary, string fullPath)
    {
        if (File.Exists(fullPath))
        {
            var backup = GetBackupPath(fullPath);
            File.Replace(temporary, fullPath, backup, ignoreMetadataErrors: true);
            File.Delete(backup);
        }
        else
        {
            File.Move(temporary, fullPath);
        }
    }

    private static string GetBackupPath(string fullPath) =>
        Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.bak");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
