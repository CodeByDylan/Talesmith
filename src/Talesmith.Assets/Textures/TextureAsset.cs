using Talesmith.Imaging;

namespace Talesmith.Assets.Textures;

/// <summary>A decoded image asset, ready to be uploaded to a renderer.</summary>
public sealed record TextureAsset(string Path, ImageData Image)
{
    public int Width => Image.Width;

    public int Height => Image.Height;

    /// <summary>Named regions from the texture's import settings; empty when the whole texture is one sprite.</summary>
    public IReadOnlyList<SpriteSlice> Sprites { get; init; } = [];

    public IReadOnlyList<SpriteAnimationInfo> Animations { get; init; } = [];

    /// <summary>The settings the texture was imported with, including how renderers should sample it.</summary>
    public TextureImportSettings Settings { get; init; } = TextureImportSettings.Default;

    public SpriteSlice? FindSprite(string name) => Sprites.FirstOrDefault(s => s.Name == name);

    public SpriteAnimationInfo? FindAnimation(string name) => Animations.FirstOrDefault(a => a.Name == name);
}
