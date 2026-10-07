using Talesmith.Imaging;

namespace Talesmith.Rendering;

/// <summary>What a renderer is and can do.</summary>
/// <param name="Backend">A short backend name such as "Skia" or "Vulkan".</param>
/// <param name="Device">The device that renders, such as a GPU name or "CPU".</param>
/// <param name="SupportsCustomShaders">Whether materials and post effects with custom shaders are applied.</param>
public sealed record RendererInfo(string Backend, string Device, int MaxTextureSize, bool SupportsCustomShaders);

/// <summary>A rendering backend: owns textures and turns <see cref="RenderFrame"/>s into pixels.</summary>
/// <remarks>
/// Texture methods are thread-safe and may be called from any thread; uploads complete before the next frame that uses them. Drawing is
/// backend-specific because the destination differs (a Skia canvas, a Vulkan image); every backend also supports
/// <see cref="IOffscreenRenderer"/> for headless rendering, screenshots and benchmarks.
/// </remarks>
public interface IRenderer : IDisposable
{
    RendererInfo Info { get; }

    /// <summary>A 1×1 opaque white texture, used to draw solid shapes.</summary>
    Texture WhiteTexture { get; }

    Texture CreateTexture(ImageData image, TextureOptions options = default);

    /// <summary>Replaces the pixels of a texture with an image of the same size.</summary>
    void UpdateTexture(Texture texture, ImageData image);

    void DestroyTexture(Texture texture);
}

/// <summary>Renders frames into images in memory, without a window.</summary>
public interface IOffscreenRenderer
{
    /// <summary>Renders a frame at the given pixel size and returns the result.</summary>
    ImageData RenderToImage(RenderFrame frame, int width, int height);

    /// <summary>Renders a frame offscreen the way a window would present it, without returning the pixels; for measuring rendering.</summary>
    void RenderOffscreen(RenderFrame frame, int width, int height);
}

/// <summary>Work a renderer did for one frame.</summary>
public readonly record struct RenderStats(int DrawCalls, int Batches, int Sprites, int MeshInstances, int PostEffects);
