using System.Text;

namespace Talesmith.EndToEnd.Tests;

/// <summary>Reads the shared libraries that a 64-bit little-endian ELF file, such as a Linux executable or <c>.so</c>, links.</summary>
internal static class ElfFile
{
    private const uint DynamicSection = 6;
    private const long EndTag = 0;
    private const long NeededTag = 1;

    /// <summary>The names in the file's <c>DT_NEEDED</c> entries, such as <c>libc.so.6</c>, which the system must provide.</summary>
    public static IReadOnlyList<string> NeededLibraries(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (reader.ReadBytes(16) is not [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, ..])
            throw new InvalidDataException($"{path} is not a 64-bit little-endian ELF file.");
        reader.BaseStream.Position = 0x28;
        var sectionTable = reader.ReadInt64();
        reader.BaseStream.Position = 0x3A;
        var sectionSize = reader.ReadUInt16();
        var sectionCount = reader.ReadUInt16();
        for (var i = 0; i < sectionCount; i++)
        {
            var section = ReadSection(reader, sectionTable + (long)i * sectionSize);
            if (section.Type != DynamicSection)
                continue;
            var strings = ReadSection(reader, sectionTable + (long)section.Link * sectionSize).Offset;
            var needed = new List<string>();
            for (var entry = section.Offset; entry < section.Offset + section.Size; entry += 16)
            {
                reader.BaseStream.Position = entry;
                var (tag, value) = (reader.ReadInt64(), reader.ReadInt64());
                if (tag == EndTag)
                    break;
                if (tag == NeededTag)
                    needed.Add(ReadString(reader, strings + value));
            }

            return needed;
        }

        return [];
    }

    private static (uint Type, long Offset, long Size, uint Link) ReadSection(BinaryReader reader, long header)
    {
        reader.BaseStream.Position = header + 4;
        var type = reader.ReadUInt32();
        reader.BaseStream.Position = header + 0x18;
        return (type, reader.ReadInt64(), reader.ReadInt64(), reader.ReadUInt32());
    }

    private static string ReadString(BinaryReader reader, long position)
    {
        reader.BaseStream.Position = position;
        var bytes = new List<byte>();
        for (var b = reader.ReadByte(); b != 0; b = reader.ReadByte())
            bytes.Add(b);
        return Encoding.ASCII.GetString([.. bytes]);
    }
}
