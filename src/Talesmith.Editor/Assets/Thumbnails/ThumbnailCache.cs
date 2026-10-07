using System.Security.Cryptography;
using System.Text;
using Talesmith.Assets;
using Talesmith.Assets.Database;

namespace Talesmith.Editor.Assets.Thumbnails;

/// <summary>Thumbnails on disk, in <c>.talesmith/thumbnails</c>, named after the asset's guid and a hash of what the thumbnail shows.</summary>
/// <remarks>The hash covers the asset's import stamp (its contents and import settings), so a changed asset never shows a stale
/// thumbnail; writing a new one deletes the old ones of the same asset. Thread-safe.</remarks>
public sealed class ThumbnailCache(string folder)
{
    /// <summary>Changes when renderers draw differently, so old thumbnails are replaced.</summary>
    public const int RendererVersion = 1;

    public string Folder { get; } = folder;

    /// <summary>The identity of an asset's current thumbnail: a hash of its import stamp, or its content hash and modification time.</summary>
    public static string Key(AssetRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var source = $"{RendererVersion}|{record.ImportStamp ?? record.ContentHash ?? record.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)).AsSpan(0, 8));
    }

    public string GetPath(AssetRecord record) => Path.Combine(Folder, $"{record.Guid}-{Key(record)}.png");

    /// <summary>The cached thumbnail of the asset as it is now, or null.</summary>
    public byte[]? TryRead(AssetRecord record)
    {
        var path = GetPath(record);
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Stores a thumbnail and removes older ones of the same asset.</summary>
    public void Write(AssetRecord record, ReadOnlySpan<byte> png)
    {
        var path = GetPath(record);
        try
        {
            Directory.CreateDirectory(Folder);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, path, overwrite: true);
            foreach (var old in Directory.EnumerateFiles(Folder, $"{record.Guid}-*.png"))
            {
                if (!string.Equals(old, path, StringComparison.Ordinal))
                    File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Deletes every thumbnail of an asset, such as when it was deleted.</summary>
    public void Remove(AssetGuid guid)
    {
        try
        {
            if (!Directory.Exists(Folder))
                return;
            foreach (var file in Directory.EnumerateFiles(Folder, $"{guid}-*.png"))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
