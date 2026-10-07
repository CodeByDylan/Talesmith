namespace Talesmith.Rendering;

/// <summary>A handle to a texture owned by an <see cref="IRenderer"/>.</summary>
/// <remarks>Handles are plain values; the renderer keeps the GPU or Skia resources. <see cref="Id"/> 0 means no texture.</remarks>
public readonly record struct Texture(int Id, int Width, int Height)
{
    public static Texture None => default;

    public bool IsNone => Id == 0;
}

/// <summary>How texels are sampled when a texture is drawn larger or smaller than its pixel size.</summary>
public enum TextureFilter
{
    /// <summary>Smooth interpolation, for painted art and scaled cameras.</summary>
    Linear,

    /// <summary>Hard pixel edges, for pixel art.</summary>
    Nearest
}

/// <summary>Options for creating a texture.</summary>
public readonly record struct TextureOptions(TextureFilter Filter = TextureFilter.Linear, string? DebugName = null);
