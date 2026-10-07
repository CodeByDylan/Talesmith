namespace Talesmith.Assets.Packs;

/// <summary>How an entry of a content pack is stored.</summary>
public enum PackCompression : byte
{
    /// <summary>The bytes as they are, for files that are already compressed, such as PNG or Ogg Vorbis.</summary>
    None = 0,

    Brotli = 1
}

/// <summary>A file in a content pack.</summary>
/// <param name="Path">The asset path, relative to the asset root, with forward slashes.</param>
/// <param name="Offset">Where the stored bytes start in the pack file.</param>
/// <param name="StoredLength">The number of stored bytes.</param>
/// <param name="Length">The file's length once decompressed.</param>
public sealed record PackEntry(string Path, long Offset, long StoredLength, long Length, PackCompression Compression);

/// <summary>The content pack format that builds write and <see cref="PackAssetSource"/> reads.</summary>
/// <remarks>
/// <para>A pack is one file: a 32-byte header, the stored files one after another, and a table of contents at the end. Numbers are
/// little-endian.</para>
/// <code>
/// header   "TSPACK\r\n" | i32 version | i32 entry count | i64 table offset | i64 table length
/// files    stored bytes of each entry, compressed or not
/// table    per entry: path (7-bit length-prefixed UTF-8) | i64 offset | i64 stored length | i64 length | u8 compression
/// </code>
/// <para>Guid references keep working because the pack carries the build's <see cref="AssetIndex"/> as an ordinary entry.</para>
/// </remarks>
public static class ContentPack
{
    /// <summary>The pack a build writes into the root of the shipped asset folder.</summary>
    public const string FileName = "content.tspack";

    public const int CurrentVersion = 1;

    internal const int HeaderSize = 32;

    internal static ReadOnlySpan<byte> Magic => "TSPACK\r\n"u8;

    /// <summary>The pack in an asset folder, or null when the folder has none.</summary>
    public static string? Find(string assetRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetRoot);
        var path = System.IO.Path.Combine(assetRoot, FileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Reads the table of contents of a pack.</summary>
    /// <exception cref="AssetException">The file is not a valid content pack.</exception>
    public static IReadOnlyList<PackEntry> ReadTable(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            stream.Position = 0;
            Span<byte> magic = stackalloc byte[8];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual(Magic))
                throw new AssetException("The file is not a Talesmith content pack.");
            var version = reader.ReadInt32();
            if (version > CurrentVersion)
                throw new AssetException($"The content pack has version {version}, but this version of Talesmith reads up to {CurrentVersion}.");
            var count = reader.ReadInt32();
            var tableOffset = reader.ReadInt64();
            var tableLength = reader.ReadInt64();
            if (count < 0 || tableOffset < HeaderSize || tableLength < 0 || tableOffset + tableLength > stream.Length)
                throw new AssetException("The content pack's table of contents is damaged.");

            stream.Position = tableOffset;
            var entries = new PackEntry[count];
            for (var i = 0; i < count; i++)
            {
                var path = reader.ReadString();
                var offset = reader.ReadInt64();
                var stored = reader.ReadInt64();
                var length = reader.ReadInt64();
                var compression = (PackCompression)reader.ReadByte();
                if (offset < HeaderSize || stored < 0 || offset + stored > tableOffset || !Enum.IsDefined(compression))
                    throw new AssetException($"The content pack entry '{path}' is damaged.");
                entries[i] = new PackEntry(path, offset, stored, length, compression);
            }

            return entries;
        }
        catch (EndOfStreamException ex)
        {
            throw new AssetException("The content pack is truncated.", ex);
        }
    }
}
