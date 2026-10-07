using SkiaSharp;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering.Skia;
using Talesmith.Rendering.Vulkan;

namespace Talesmith.Rendering.Tests;

/// <summary>The backends available on this machine, for tests that render the same frame with each.</summary>
public static class OffscreenRenderers
{
    private static readonly Lazy<bool> VulkanAvailable = new(() =>
    {
        try
        {
            using var renderer = VulkanRenderer.Create();
            return true;
        }
        catch (VulkanUnavailableException)
        {
            return false;
        }
    });

    public static TheoryData<string> Backends
    {
        get
        {
            var backends = new TheoryData<string> { "skia" };
            if (VulkanAvailable.Value)
                backends.Add("vulkan");
            return backends;
        }
    }

    public static IRenderer Create(string backend) => backend switch
    {
        "skia" => new SkiaRenderer(),
        "vulkan" => VulkanRenderer.Create(),
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, null)
    };

    /// <summary>Writes an image to the test output's captures folder, so renders can be inspected, and returns its path.</summary>
    public static string Save(ImageData image, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "captures", "lighting");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name + ".png");
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var skImage = SKImage.FromPixelCopy(info, image.Pixels.AsSpan(), image.Stride);
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    public static Color Pixel(ImageData image, int x, int y)
    {
        var i = y * image.Stride + x * 4;
        return new Color(image.Pixels[i], image.Pixels[i + 1], image.Pixels[i + 2], image.Pixels[i + 3]);
    }

    public static float Brightness(Color color) => (color.R + color.G + color.B) / (3f * 255);
}
