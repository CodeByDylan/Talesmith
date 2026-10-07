using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Talesmith.Editor.Shell;

/// <summary>Lays out the tool options bar: the active tool and its options on the left and the view's options on the right, in one row while both
/// fit, the view's toggles showing only their icons when that makes them fit, else in two rows with the view's options on the second, still on the
/// right, so that every option stays in reach.</summary>
/// <remarks>The first child is the tool's part and the second the view's part. The class <c>compact</c> on this panel marks the form whose view
/// toggles show only their icons, for the bar's styles.</remarks>
public sealed class ToolStripPanel : Panel
{
    public static readonly StyledProperty<double> RowHeightProperty =
        AvaloniaProperty.Register<ToolStripPanel, double>(nameof(RowHeight), 37);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ToolStripPanel, double>(nameof(Spacing), 16);

    private const string Compact = "compact";

    private bool _wraps;

    static ToolStripPanel() => AffectsMeasure<ToolStripPanel>(RowHeightProperty, SpacingProperty);

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>Gets or sets the least space between the two parts when they share a row.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gets whether the view's options are on a row of their own.</summary>
    public bool Wraps => _wraps;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
            return default;

        var (tool, view) = (Children[0], Children[1]);
        tool.Measure(Size.Infinity);
        _wraps = !FitsBeside(tool, view, availableSize.Width, compact: false) && !FitsBeside(tool, view, availableSize.Width, compact: true);
        if (_wraps)
        {
            SetCompact(view, false);
            view.Measure(Size.Infinity);
            if (view.DesiredSize.Width > availableSize.Width)
                SetCompact(view, true);
        }

        var row = RowHeight;
        view.Measure(Size.Infinity);
        var viewWidth = Math.Min(view.DesiredSize.Width, availableSize.Width);
        view.Measure(new Size(viewWidth, row));
        tool.Measure(new Size(_wraps ? availableSize.Width : availableSize.Width - viewWidth - Spacing, row));
        var width = _wraps ? Math.Max(tool.DesiredSize.Width, viewWidth) : tool.DesiredSize.Width + Spacing + viewWidth;
        return new Size(width, _wraps ? 2 * row : row);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
            return finalSize;

        var (tool, view) = (Children[0], Children[1]);
        var row = RowHeight;
        var viewWidth = Math.Min(view.DesiredSize.Width, finalSize.Width);
        tool.Arrange(new Rect(0, 0, _wraps ? finalSize.Width : Math.Max(0, finalSize.Width - viewWidth - Spacing), row));
        view.Arrange(new Rect(finalSize.Width - viewWidth, _wraps ? row : 0, viewWidth, row));
        return finalSize;
    }

    private bool FitsBeside(Control tool, Control view, double width, bool compact)
    {
        SetCompact(view, compact);
        view.Measure(Size.Infinity);
        return tool.DesiredSize.Width + Spacing + view.DesiredSize.Width <= width;
    }

    /// <summary>Sets the compact form; when it changes, the view's controls are measured again, as a control hidden or shown by a style only tells
    /// its parent on the next layout pass.</summary>
    private void SetCompact(Control view, bool compact)
    {
        if (Classes.Contains(Compact) == compact)
            return;
        Classes.Set(Compact, compact);
        foreach (var layoutable in view.GetSelfAndVisualDescendants().OfType<Layoutable>())
            layoutable.InvalidateMeasure();
    }
}
