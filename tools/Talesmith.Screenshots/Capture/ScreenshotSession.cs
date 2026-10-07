using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Talesmith.Avalonia.Hosting;
using Talesmith.UI.Theming;

namespace Talesmith.Screenshots.Capture;

/// <summary>The file format screenshots are saved in.</summary>
internal enum ImageFormat
{
    Png,
    Webp
}

/// <summary>Renders scenes headlessly and saves them in the dark and light themes.</summary>
internal sealed class ScreenshotSession(string outputDirectory, double scale, ImageFormat format = ImageFormat.Png)
{
    private const int WebpQuality = 86;

    private static readonly (ThemeMode Mode, string Suffix)[] Themes = [(ThemeMode.Dark, "dark"), (ThemeMode.Light, "light")];

    public static void InitializeAvalonia()
    {
        NativeLibraryIsolation.Apply();
        AppBuilder.Configure<ScreenshotApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont()
            .SetupWithoutStarting();
    }

    public int Count { get; private set; }

    /// <summary>Saves the scene as <c>{name}.dark</c> and <c>{name}.light</c>, where the name defaults to the scene's.</summary>
    /// <returns>The pixel size of the saved images.</returns>
    public PixelSize Capture(ScreenshotScene scene, string? name = null)
    {
        name ??= scene.Name;
        var size = default(PixelSize);
        using var theme = new ThemeManager(Application.Current!);
        foreach (var (mode, suffix) in Themes)
        {
            theme.Mode = mode;
            var window = new Window { Content = scene.Build(), WindowDecorations = WindowDecorations.None };
            RenderLoop.Host(window, scene.Size.Width, scene.Size.Height, scale);
            RenderLoop.Settle(250);
            scene.Prepare(window);
            RenderLoop.Settle(500);
            using (var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window did not render."))
            {
                var region = scene.Region(window);
                using var cropped = region is null ? null : Crop(frame, Bounds(window, region));
                var image = cropped ?? frame;
                size = image.PixelSize;
                Save(image, $"{name}.{suffix}");
            }

            window.Close();
            scene.Release();
        }

        Count++;
        Console.WriteLine($"  {name}");
        return size;
    }

    /// <summary>Renders a window once at its logical size and saves it as a PNG.</summary>
    public static void CaptureImage(Window window, double width, double height, string path)
    {
        RenderLoop.Host(window, width, height, 1);
        RenderLoop.Settle(400);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("The window did not render.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        frame.Save(path, PngBitmapEncoderOptions.Default);
        window.Close();
    }

    private static PixelRect Bounds(Window window, Control region)
    {
        var transform = region.TransformToVisual(window) ?? throw new InvalidOperationException($"{region.GetType().Name} is not in the window.");
        var rect = new Rect(region.Bounds.Size).TransformToAABB(transform);
        return PixelRect.FromRect(rect, window.RenderScaling);
    }

    private static WriteableBitmap Crop(WriteableBitmap frame, PixelRect area)
    {
        using var source = frame.Lock();
        area = area.Intersect(new PixelRect(source.Size));
        var cropped = new WriteableBitmap(area.Size, frame.Dpi, source.Format, AlphaFormat.Premul);
        using var target = cropped.Lock();
        var row = new byte[area.Width * 4];
        for (var y = 0; y < area.Height; y++)
        {
            Marshal.Copy(source.Address + (area.Y + y) * source.RowBytes + area.X * 4, row, 0, row.Length);
            Marshal.Copy(row, 0, target.Address + y * target.RowBytes, row.Length);
        }

        return cropped;
    }

    private void Save(WriteableBitmap image, string baseName)
    {
        var path = Path.Combine(outputDirectory, $"{baseName}.{(format == ImageFormat.Webp ? "webp" : "png")}");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (format == ImageFormat.Png)
        {
            image.Save(path, PngBitmapEncoderOptions.Default);
            return;
        }

        using var pixels = image.Lock();
        var colorType = pixels.Format == PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
        var info = new SKImageInfo(pixels.Size.Width, pixels.Size.Height, colorType, SKAlphaType.Premul);
        using var skia = SKImage.FromPixels(info, pixels.Address, pixels.RowBytes);
        using var data = skia.Encode(SKEncodedImageFormat.Webp, WebpQuality);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
