using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Talesmith.Editor.Viewport;

/// <summary>The viewport's surface for the controls floating over the scene, laid out as in a <see cref="Panel"/> except that the tool rail
/// stays clear of the zoom controls.</summary>
/// <remarks>When the viewport is too small for both side by side, the rail gets only the height above the zoom controls, so it continues in more
/// columns instead of hiding tools behind them. Zoom controls wider than the viewport get the <c>compact</c> class, and then the <c>minimal</c>
/// one, which leave out their least needed buttons.</remarks>
public sealed class ViewportChrome : Panel
{
    private const double Gap = 6;

    private static readonly string[] ZoomForms = ["compact", "minimal"];

    private readonly double[] _zoomWidths = [double.NaN, double.NaN, double.NaN];

    /// <summary>The tool rail, aligned to the top left.</summary>
    public Control? Rail { get; set; }

    /// <summary>The zoom controls, aligned to the bottom right.</summary>
    public Control? Zoom { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        if (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
            return size;

        if (Zoom is { IsVisible: true } zoom)
        {
            FitZoom(zoom, availableSize);
            if (Rail is { IsVisible: true } rail && Place(rail, availableSize).Intersects(Place(zoom, availableSize).Inflate(Gap)))
                rail.Measure(availableSize.WithHeight(Math.Max(0, Place(zoom, availableSize).Top - Gap)));
        }

        return size;
    }

    /// <summary>Gives the zoom controls their fullest form that fits, remembering each form's width so the forms are not tried again on every
    /// layout pass.</summary>
    private void FitZoom(Control zoom, Size available)
    {
        var current = Array.FindLastIndex(ZoomForms, zoom.Classes.Contains) + 1;
        _zoomWidths[current] = zoom.DesiredSize.Width;
        for (var form = 0; form < _zoomWidths.Length; form++)
        {
            if (double.IsNaN(_zoomWidths[form]))
                _zoomWidths[form] = MeasureForm(zoom, form, available);
            if (_zoomWidths[form] <= available.Width - Gap || form == _zoomWidths.Length - 1)
            {
                if (form != Array.FindLastIndex(ZoomForms, zoom.Classes.Contains) + 1)
                    MeasureForm(zoom, form, available);
                return;
            }
        }
    }

    private static double MeasureForm(Control zoom, int form, Size available)
    {
        for (var i = 0; i < ZoomForms.Length; i++)
            zoom.Classes.Set(ZoomForms[i], i < form);
        zoom.Measure(available);
        return zoom.DesiredSize.Width;
    }

    /// <summary>Where a child's alignment puts it, without its margin, as <see cref="Panel"/> arranges it.</summary>
    private static Rect Place(Control child, Size available)
    {
        var margin = child.Margin;
        var desired = child.DesiredSize;
        var x = child.HorizontalAlignment switch
        {
            HorizontalAlignment.Right => available.Width - desired.Width,
            HorizontalAlignment.Center => (available.Width - desired.Width) / 2,
            _ => 0
        };
        var y = child.VerticalAlignment switch
        {
            VerticalAlignment.Bottom => available.Height - desired.Height,
            VerticalAlignment.Center => (available.Height - desired.Height) / 2,
            _ => 0
        };
        return new Rect(x + margin.Left, y + margin.Top, Math.Max(0, desired.Width - margin.Left - margin.Right),
            Math.Max(0, desired.Height - margin.Top - margin.Bottom));
    }
}
