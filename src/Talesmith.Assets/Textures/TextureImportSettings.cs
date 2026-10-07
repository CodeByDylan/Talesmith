using System.Numerics;
using Talesmith.Rendering;

namespace Talesmith.Assets.Textures;

/// <summary>How a texture repeats outside its bounds.</summary>
public enum TextureWrapMode
{
    Clamp,
    Repeat,
    Mirror
}

/// <summary>How strongly a build may compress a texture.</summary>
public enum TextureCompression
{
    None,
    Normal,
    HighQuality
}

/// <summary>Whether a texture is one sprite or is cut into several.</summary>
public enum SpriteMode
{
    /// <summary>The whole texture is one sprite.</summary>
    Single,

    /// <summary>The texture is a sprite sheet cut by <see cref="TextureImportSettings.Grid"/> and <see cref="TextureImportSettings.Slices"/>.</summary>
    Multiple
}

/// <summary>Cuts a sprite sheet into equally sized cells, read left to right, top to bottom.</summary>
/// <param name="CellWidth">The width of a cell in pixels.</param>
/// <param name="CellHeight">The height of a cell in pixels.</param>
public sealed record SpriteGrid(int CellWidth, int CellHeight)
{
    /// <summary>Pixels from the left edge to the first column.</summary>
    public int OffsetX { get; init; }

    /// <summary>Pixels from the top edge to the first row.</summary>
    public int OffsetY { get; init; }

    public int SpacingX { get; init; }

    public int SpacingY { get; init; }

    /// <summary>The number of columns; 0 fits as many as the texture holds.</summary>
    public int Columns { get; init; }

    /// <summary>The number of rows; 0 fits as many as the texture holds.</summary>
    public int Rows { get; init; }

    /// <summary>The name of each cell before its index; null uses the file name followed by an underscore.</summary>
    public string? NamePrefix { get; init; }

    public Vector2 Pivot { get; init; } = new(0.5f);

    /// <summary>Leaves out cells whose pixels are all transparent.</summary>
    public bool SkipEmpty { get; init; } = true;
}

/// <summary>Import settings of textures, stored in their .meta files.</summary>
public sealed record TextureImportSettings
{
    public static TextureImportSettings Default { get; } = new();

    /// <summary>The sampling filter; null uses the game's default.</summary>
    public TextureFilter? Filter { get; init; }

    public TextureWrapMode Wrap { get; init; } = TextureWrapMode.Clamp;

    /// <summary>Whether renderers should generate mipmaps for the texture when they support them.</summary>
    public bool Mipmaps { get; init; }

    /// <summary>Whether the file's colors are already multiplied by alpha, so importing keeps them as they are.</summary>
    public bool PremultipliedAlpha { get; init; }

    /// <summary>The largest width or height; bigger images are scaled down, with their slices. 0 keeps the original size.</summary>
    public int MaxSize { get; init; }

    public TextureCompression Compression { get; init; } = TextureCompression.None;

    public SpriteMode SpriteMode { get; init; } = SpriteMode.Single;

    /// <summary>For <see cref="SpriteMode.Multiple"/>, cuts the texture into cells.</summary>
    public SpriteGrid? Grid { get; init; }

    /// <summary>For <see cref="SpriteMode.Multiple"/>, named regions in addition to the grid's; a slice replaces a grid cell with its name.</summary>
    public IReadOnlyList<SpriteSlice> Slices { get; init; } = [];

    /// <summary>Animations made of the texture's sprites.</summary>
    public IReadOnlyList<SpriteAnimationInfo> Animations { get; init; } = [];
}
