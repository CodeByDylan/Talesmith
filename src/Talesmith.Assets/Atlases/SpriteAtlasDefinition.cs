namespace Talesmith.Assets.Atlases;

/// <summary>The contents of a <c>.tatlas</c> file: which textures to pack into one atlas, and how.</summary>
/// <remarks>
/// <code>
/// {
///   "version": 1,
///   "sources": [ "9a2c4e0b7d1f4a3c8e6b5d4c3b2a1f0e", "sprites/ui" ],
///   "padding": 2,
///   "trim": true,
///   "powerOfTwo": false,
///   "maxSize": 4096
/// }
/// </code>
/// Each source is the guid of a texture or folder, or a path relative to the atlas file. Folders contribute every texture inside them.
/// </remarks>
public sealed record SpriteAtlasDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>Empty pixels between sprites and around the border, which keeps filtering from bleeding neighbors in.</summary>
    public int Padding { get; init; } = 2;

    /// <summary>Removes fully transparent borders from each sprite; pivots are adjusted so sprites stay anchored where they were.</summary>
    public bool Trim { get; init; } = true;

    public bool PowerOfTwo { get; init; }

    public int MaxSize { get; init; } = 4096;
}
