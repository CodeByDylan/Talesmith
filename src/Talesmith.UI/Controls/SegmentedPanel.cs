using Avalonia;
using Avalonia.Controls;

namespace Talesmith.UI.Controls;

/// <summary>Lays out the segments of a <see cref="SegmentedControl"/> as cells of equal width in one row, and continues on more rows when the row is too
/// narrow for them, so that no segment is narrower than its content.</summary>
public class SegmentedPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var cell = default(Size);
        var count = 0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            if (!child.IsVisible)
                continue;
            cell = new Size(Math.Max(cell.Width, child.DesiredSize.Width), Math.Max(cell.Height, child.DesiredSize.Height));
            count++;
        }

        if (count == 0)
            return default;
        var columns = Columns(count, cell.Width, availableSize.Width);
        return new Size(columns * cell.Width, Math.Ceiling(count / (double)columns) * cell.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var segments = Children.Where(c => c.IsVisible).ToList();
        if (segments.Count == 0)
            return finalSize;
        var columns = Columns(segments.Count, segments.Max(s => s.DesiredSize.Width), finalSize.Width);
        var width = finalSize.Width / columns;
        var height = segments.Max(s => s.DesiredSize.Height);
        for (var i = 0; i < segments.Count; i++)
            segments[i].Arrange(new Rect(i % columns * width, i / columns * height, width, height));
        return finalSize;
    }

    // Half a pixel of tolerance keeps layout rounding from wrapping a row that fits.
    private static int Columns(int count, double cellWidth, double width) =>
        double.IsInfinity(width) || cellWidth <= 0 ? count : Math.Clamp((int)((width + 0.5) / cellWidth), 1, count);
}
