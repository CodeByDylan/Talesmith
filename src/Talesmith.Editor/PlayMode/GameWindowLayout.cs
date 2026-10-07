using Avalonia;

namespace Talesmith.Editor.PlayMode;

/// <summary>Where a <see cref="GameWindowFrame"/> shows its window and how large, in the frame's logical pixels.</summary>
/// <param name="ContentSize">The size the content lays out at, in logical pixels of the window's display.</param>
/// <param name="ContentScale">The scale that shows the content at the zoom: the frame's logical pixels per logical pixel of the display.</param>
/// <param name="Window">The window's area, inside the ring.</param>
/// <param name="Zoom">Screen pixels per window pixel.</param>
/// <param name="DesiredSize">The frame's size for the window, its ring and margin.</param>
internal readonly record struct GameWindowLayout(Size ContentSize, double ContentScale, Rect Window, double Zoom, Size DesiredSize)
{
    private const double SmallestZoom = 0.01;

    /// <summary>Lays out the window in <paramref name="room"/>: centered when it fits and at the margin when it does not, where a scroll
    /// viewer around the frame scrolls it. Without a window, the content fills the room inside the ring.</summary>
    /// <param name="room">The frame's room; infinite along a direction a scroll viewer scrolls.</param>
    /// <param name="renderScaling">Screen pixels per logical pixel of the frame.</param>
    /// <param name="zoom">Screen pixels per window pixel, or null for as large as fits, up to 1.</param>
    /// <param name="ring">The width of the ring around the window.</param>
    /// <param name="margin">The space around the ring of a window, which a filling window does not get.</param>
    public static GameWindowLayout Compute(Size room, double renderScaling, GameWindowSize? window, double? zoom, double ring, double margin)
    {
        if (window is not { } shown)
        {
            var filled = new Size(Finite(room.Width), Finite(room.Height));
            var inside = new Rect(filled).Deflate(ring);
            return new GameWindowLayout(inside.Size, 1, inside, 1, filled);
        }

        var edge = ring + margin;
        var actual = zoom ?? Fit(room, renderScaling, shown, edge);
        var size = new Size(shown.Width * actual / renderScaling, shown.Height * actual / renderScaling);
        var desired = new Size(size.Width + 2 * edge, size.Height + 2 * edge);
        var position = new Point(Snap(Start(room.Width, size.Width, edge), renderScaling), Snap(Start(room.Height, size.Height, edge), renderScaling));
        return new GameWindowLayout(new Size(shown.Width / shown.DisplayScale, shown.Height / shown.DisplayScale), actual * shown.DisplayScale / renderScaling,
            new Rect(position, size), actual, desired);
    }

    private static double Fit(Size room, double renderScaling, GameWindowSize window, double edge)
    {
        var width = (room.Width - 2 * edge) * renderScaling / window.Width;
        var height = (room.Height - 2 * edge) * renderScaling / window.Height;
        var fit = Math.Min(double.IsFinite(width) ? width : 1, double.IsFinite(height) ? height : 1);
        return Math.Clamp(fit, SmallestZoom, 1);
    }

    private static double Start(double room, double size, double edge) => double.IsFinite(room) ? Math.Max(edge, (room - size) / 2) : edge;

    private static double Snap(double value, double renderScaling) => Math.Round(value * renderScaling) / renderScaling;

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
