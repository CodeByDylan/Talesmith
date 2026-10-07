using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Talesmith.Screenshots.Capture;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Screenshots.Docs;

/// <summary>Renders the 1200 × 630 link preview card of the documentation site.</summary>
internal static class SocialCard
{
    private const double Width = 1200;
    private const double Height = 630;

    public static void Render(string screenshot, string path)
    {
        using var editor = LoadBitmap(screenshot);
        ScreenshotSession.CaptureImage(new Window { Content = Card(editor), Background = Brushes.Black }, Width, Height, path);
        Console.WriteLine($"  {Path.GetFileName(path)}");
    }

    private static Panel Card(Bitmap editor)
    {
        var background = new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#14152B"), 0), new GradientStop(Color.Parse("#0E0F12"), 0.55), new GradientStop(Color.Parse("#1C1236"), 1) }
            }
        };

        var glow = new global::Avalonia.Controls.Shapes.Ellipse
        {
            Width = 760,
            Height = 760,
            Margin = new Thickness(0, -260, -260, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.Parse("#555B5BEF"), 0), new GradientStop(Color.Parse("#005B5BEF"), 1) } }
        };

        var shot = new Border
        {
            Width = 720,
            Margin = new Thickness(0, 96, -170, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            CornerRadius = new CornerRadius(14),
            ClipToBounds = true,
            BorderBrush = new SolidColorBrush(Color.Parse("#33363E")),
            BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 30 80 0 #CC000000"),
            Child = new Image { Source = editor, Stretch = Stretch.Uniform }
        };

        var text = new StackPanel
        {
            Margin = new Thickness(72, 0, 0, 0),
            Width = 450,
            Spacing = 22,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Logo(56), Label("Talesmith", 50, FontWeight.Bold, "#FFFFFF") } },
                Label("Make 2D games in C#, from the first tile to the finished build.", 34, FontWeight.SemiBold, "#ECEDF0", wrap: true),
                Label("The documentation for the Talesmith engine, editor and plugins.", 21, FontWeight.Normal, "#A7ABB5", wrap: true)
            }
        };

        return new Panel { Width = Width, Height = Height, ClipToBounds = true, Children = { background, glow, shot, text } };
    }

    /// <summary>The app bar's mark: the hammer icon on a rounded indigo square.</summary>
    private static Border Logo(double size) => new()
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size * 0.27),
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#8B8EFA"), 0), new GradientStop(Color.Parse("#5B4FE0"), 1) }
        },
        Child = new SymbolIcon { Data = Icons.Hammer, Size = size * 0.6, StrokeThickness = 2.25, Foreground = Brushes.White }
    };

    private static TextBlock Label(string text, double size, FontWeight weight, string color, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = new SolidColorBrush(Color.Parse(color)),
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
        LineHeight = size * 1.25
    };

    private static Bitmap LoadBitmap(string path)
    {
        using var decoded = SKBitmap.Decode(path);
        using var data = decoded.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
