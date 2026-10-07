using Avalonia.Media;
using Avalonia.Media.Immutable;
using EngineColor = Talesmith.Mathematics.Color;

namespace Talesmith.UI.Controls;

/// <summary>Converts between engine colors and Avalonia colors.</summary>
public static class EngineColors
{
    public static Color ToAvalonia(this EngineColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static EngineColor ToEngine(this Color color) => new(color.R, color.G, color.B, color.A);

    /// <summary>Draws a checkerboard so translucent colors drawn over it read as translucent.</summary>
    internal static void DrawChecker(DrawingContext context, Avalonia.Rect area, double cell = 5)
    {
        context.FillRectangle(CheckerLight, area);
        var rows = (int)Math.Ceiling(area.Height / cell);
        var columns = (int)Math.Ceiling(area.Width / cell);
        for (var y = 0; y < rows; y++)
        {
            for (var x = y % 2; x < columns; x += 2)
            {
                var rect = new Avalonia.Rect(area.X + x * cell, area.Y + y * cell, cell, cell).Intersect(area);
                context.FillRectangle(CheckerDark, rect);
            }
        }
    }

    private static readonly IBrush CheckerLight = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly IBrush CheckerDark = new ImmutableSolidColorBrush(Color.FromRgb(0xD4, 0xD7, 0xDD));
}
