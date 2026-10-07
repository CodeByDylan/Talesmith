using System.Buffers.Binary;
using System.Text;

namespace Talesmith.Assets.Fonts;

/// <summary>Reads the family and style names from the name table of a TrueType, OpenType or font collection file.</summary>
/// <remarks>Typographic names (ids 16 and 17) win over the legacy ones (1 and 2), and English Windows names over other platforms.</remarks>
public static class OpenTypeNames
{
    private const ushort FamilyId = 1;
    private const ushort SubfamilyId = 2;
    private const ushort TypographicFamilyId = 16;
    private const ushort TypographicSubfamilyId = 17;

    /// <exception cref="AssetException">The data is not a font or has no family name.</exception>
    public static (string Family, string Style) Read(ReadOnlySpan<byte> font)
    {
        try
        {
            var offset = 0;
            if (font.Length >= 16 && font[..4].SequenceEqual("ttcf"u8))
                offset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(font[12..]));

            var version = BinaryPrimitives.ReadUInt32BigEndian(font[offset..]);
            if (version is not (0x00010000 or 0x4F54544F or 0x74727565))
                throw new AssetException("The file is not a TrueType or OpenType font.");

            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(font[(offset + 4)..]);
            for (var i = 0; i < tableCount; i++)
            {
                var record = font.Slice(offset + 12 + i * 16, 16);
                if (!record[..4].SequenceEqual("name"u8))
                    continue;

                var tableOffset = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]));
                var tableLength = checked((int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]));
                return ReadNameTable(font.Slice(tableOffset, tableLength));
            }

            throw new AssetException("The font has no name table.");
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            throw new AssetException("The font file is damaged.", ex);
        }
    }

    private static (string Family, string Style) ReadNameTable(ReadOnlySpan<byte> table)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(table[2..]);
        var storage = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
        var best = new Dictionary<ushort, (int Score, string Value)>();
        for (var i = 0; i < count; i++)
        {
            var record = table.Slice(6 + i * 12, 12);
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            if (nameId is not (FamilyId or SubfamilyId or TypographicFamilyId or TypographicSubfamilyId))
                continue;

            var score = (platform, language) switch
            {
                (3, 0x409) => 4,
                (3, _) => 3,
                (0, _) => 2,
                (1, 0) => 1,
                _ => 0
            };
            if (score == 0 || (best.TryGetValue(nameId, out var current) && current.Score >= score))
                continue;

            var length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);
            var bytes = table.Slice(storage + stringOffset, length);
            var value = (platform == 1 ? Encoding.Latin1.GetString(bytes) : Encoding.BigEndianUnicode.GetString(bytes)).Trim('\0', ' ');
            if (value.Length > 0)
                best[nameId] = (score, value);
        }

        var family = Pick(best, TypographicFamilyId, FamilyId) ?? throw new AssetException("The font has no family name.");
        return (family, Pick(best, TypographicSubfamilyId, SubfamilyId) ?? "Regular");
    }

    private static string? Pick(Dictionary<ushort, (int Score, string Value)> names, ushort preferred, ushort fallback) =>
        names.TryGetValue(preferred, out var name) ? name.Value : names.TryGetValue(fallback, out name) ? name.Value : null;
}
