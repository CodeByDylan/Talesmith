namespace Talesmith.Assets.Fonts;

/// <summary>Import settings of fonts, stored in their .meta files.</summary>
public sealed record FontImportSettings
{
    /// <summary>The family name to register the font under; null uses the name stored in the file.</summary>
    public string? FamilyName { get; init; }

    /// <summary>The size in pixels text uses when it does not choose one.</summary>
    public float DefaultSize { get; init; } = 16;

    /// <summary>Fonts tried in order for characters this font does not have.</summary>
    public IReadOnlyList<AssetGuid> Fallbacks { get; init; } = [];
}

/// <summary>A TrueType or OpenType font file, kept in memory so hosts can register it with their text renderer.</summary>
/// <param name="family">The family name, such as "Inter", from the settings or the file's name table.</param>
/// <param name="style">The style within the family, such as "Bold Italic".</param>
public sealed class FontAsset(string path, byte[] data, string family, string style, FontImportSettings settings)
{
    private readonly byte[] _data = data;

    public string Path { get; } = path;

    public string Family { get; } = family;

    public string Style { get; } = style;

    public FontImportSettings Settings { get; } = settings;

    /// <summary>The font file's bytes.</summary>
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>Opens the font file's bytes as a read-only stream.</summary>
    public Stream OpenRead() => new MemoryStream(_data, writable: false);

    public override string ToString() => $"{Family} {Style}";
}
