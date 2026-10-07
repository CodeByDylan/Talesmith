using Avalonia;
using Avalonia.Controls;

namespace Talesmith.UI.Controls;

/// <summary>A toolbar of two parts, such as a panel's filters and its search box, on one row while both fit, the second at the right; else each on a
/// row of its own, as wide as the bar.</summary>
/// <remarks>
/// On one row, one part is at its own width and the other takes the rest of the row: the second by default, so that content such as a search box
/// with a <see cref="Avalonia.Layout.Layoutable.MinWidth"/> grows into it, or the first with <see cref="FirstGrows"/>, which then needs only its
/// <see cref="Avalonia.Layout.Layoutable.MinWidth"/> to share the row. A part that is a <see cref="WrapPanel"/> wraps when its row is narrower than
/// it.
/// </remarks>
public sealed class SplitBar : Panel
{
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SplitBar, double>(nameof(Spacing), 8);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<SplitBar, double>(nameof(RowSpacing), 6);

    public static readonly StyledProperty<bool> FirstGrowsProperty =
        AvaloniaProperty.Register<SplitBar, bool>(nameof(FirstGrows));

    private bool _wraps;

    static SplitBar() => AffectsMeasure<SplitBar>(SpacingProperty, RowSpacingProperty, FirstGrowsProperty);

    /// <summary>Gets or sets the least space between the two parts when they share a row.</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>Gets or sets the space between the rows when the parts are on rows of their own.</summary>
    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>Gets or sets whether the first part, rather than the second, takes what the row has left, such as breadcrumbs that show more of
    /// a path given the room.</summary>
    public bool FirstGrows
    {
        get => GetValue(FirstGrowsProperty);
        set => SetValue(FirstGrowsProperty, value);
    }

    /// <summary>Gets whether the second part is on a row of its own.</summary>
    public bool Wraps => _wraps;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
            return default;
        var first = Children[0];
        if (Children.Count == 1)
        {
            first.Measure(availableSize);
            return first.DesiredSize;
        }

        var second = Children[1];
        first.Measure(Size.Infinity);
        second.Measure(Size.Infinity);
        var (leading, trailing) = (FirstGrows ? first.MinWidth : first.DesiredSize.Width, second.DesiredSize.Width);
        _wraps = leading + Spacing + trailing > availableSize.Width;
        if (_wraps)
        {
            first.Measure(availableSize.WithHeight(double.PositiveInfinity));
            second.Measure(availableSize.WithHeight(double.PositiveInfinity));
            return new Size(Math.Max(first.DesiredSize.Width, second.DesiredSize.Width), first.DesiredSize.Height + RowSpacing + second.DesiredSize.Height);
        }

        var rest = availableSize.Width - Spacing;
        if (FirstGrows)
            first.Measure(new Size(Math.Max(leading, rest - trailing), double.PositiveInfinity));
        else
            second.Measure(new Size(Math.Max(trailing, rest - first.DesiredSize.Width), double.PositiveInfinity));
        return new Size(first.DesiredSize.Width + Spacing + second.DesiredSize.Width, Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
            return finalSize;
        var first = Children[0];
        if (Children.Count == 1)
        {
            first.Arrange(new Rect(finalSize));
            return finalSize;
        }

        var second = Children[1];
        if (_wraps)
        {
            var top = first.DesiredSize.Height;
            first.Arrange(new Rect(0, 0, finalSize.Width, top));
            second.Arrange(new Rect(0, top + RowSpacing, finalSize.Width, Math.Max(0, finalSize.Height - top - RowSpacing)));
            return finalSize;
        }

        var leading = FirstGrows ? Math.Max(0, finalSize.Width - Spacing - second.DesiredSize.Width) : first.DesiredSize.Width;
        first.Arrange(new Rect(0, 0, leading, finalSize.Height));
        second.Arrange(new Rect(leading + Spacing, 0, Math.Max(0, finalSize.Width - leading - Spacing), finalSize.Height));
        return finalSize;
    }
}
