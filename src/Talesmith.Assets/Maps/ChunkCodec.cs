using System.Buffers.Binary;
using System.IO.Compression;

namespace Talesmith.Assets.Maps;

/// <summary>Converts chunk cells to and from zlib-compressed little-endian 32-bit values, the chunk encoding of .hexy maps.</summary>
public static class ChunkCodec
{
    /// <exception cref="AssetException">The data is not valid or holds fewer cells than expected.</exception>
    public static TileCell[] Decode(ReadOnlySpan<byte> compressed, int cellCount)
    {
        var raw = new byte[cellCount * sizeof(uint)];
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed.ToArray(), writable: false), CompressionMode.Decompress);
            zlib.ReadExactly(raw);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new AssetException($"A chunk could not be decoded: {ex.Message}", ex);
        }

        var cells = new TileCell[cellCount];
        for (var i = 0; i < cellCount; i++)
            cells[i] = new TileCell(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * sizeof(uint))));
        return cells;
    }

    public static byte[] Encode(ReadOnlySpan<TileCell> cells)
    {
        var raw = new byte[cells.Length * sizeof(uint)];
        for (var i = 0; i < cells.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i * sizeof(uint)), cells[i].Raw);
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);
        return output.ToArray();
    }
}
