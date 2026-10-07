using Avalonia;
using Avalonia.Controls;

namespace Talesmith.UI.Docking;

/// <summary>Lays out <see cref="DockTab"/>s in a row; when they do not fit, inactive tabs show only their icon, then tabs shrink to <see cref="MinimumTabWidth"/> and finally those that do not fit are hidden.</summary>
/// <remarks>The active tab is always shown; it takes the place of the last tab that fits.</remarks>
public sealed class DockTabStrip : Panel
{
    public static readonly StyledProperty<double> MinimumTabWidthProperty =
        AvaloniaProperty.Register<DockTabStrip, double>(nameof(MinimumTabWidth), 72);

    public static readonly DirectProperty<DockTabStrip, bool> IsOverflowingProperty =
        AvaloniaProperty.RegisterDirect<DockTabStrip, bool>(nameof(IsOverflowing), s => s.IsOverflowing);

    private const double CompactTabWidth = 34;

    private bool _isOverflowing;
    private double[] _widths = [];

    static DockTabStrip()
    {
        AffectsMeasure<DockTabStrip>(MinimumTabWidthProperty);
    }

    public double MinimumTabWidth
    {
        get => GetValue(MinimumTabWidthProperty);
        set => SetValue(MinimumTabWidthProperty, value);
    }

    /// <summary>Gets whether some tabs are hidden for lack of space.</summary>
    public bool IsOverflowing
    {
        get => _isOverflowing;
        private set => SetAndRaise(IsOverflowingProperty, ref _isOverflowing, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        if (_widths.Length != count)
            _widths = new double[count];

        var height = 0.0;
        var total = 0.0;
        var active = -1;
        for (var i = 0; i < count; i++)
        {
            var child = Children[i];
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            _widths[i] = child is DockTab { IsCompact: true, FullWidth: > 0 } compact ? compact.FullWidth : child.DesiredSize.Width;
            total += _widths[i];
            height = Math.Max(height, child.DesiredSize.Height);
            if (child is DockTab { IsActive: true })
                active = i;
        }

        var available = availableSize.Width;
        var compacted = new bool[count];
        for (var i = count - 1; i >= 0 && total > available; i--)
        {
            if (i == active || Children[i] is not DockTab { Icon: not null } || _widths[i] <= CompactTabWidth)
                continue;
            total -= _widths[i] - CompactTabWidth;
            _widths[i] = CompactTabWidth;
            compacted[i] = true;
        }

        for (var i = 0; i < count; i++)
        {
            if (Children[i] is DockTab tab)
                tab.IsCompact = compacted[i];
        }

        if (double.IsInfinity(available) || total <= available)
        {
            MarkOverflow(count, -1);
            MeasureAt(availableSize.Height);
            return new Size(total, height);
        }

        var shrinkable = 0.0;
        for (var i = 0; i < count; i++)
            shrinkable += Math.Max(0, _widths[i] - MinimumTabWidth);
        var excess = total - available;

        int visible;
        if (excess <= shrinkable)
        {
            var ratio = shrinkable <= 0 ? 0 : excess / shrinkable;
            for (var i = 0; i < count; i++)
                _widths[i] -= Math.Max(0, _widths[i] - MinimumTabWidth) * ratio;
            visible = count;
        }
        else
        {
            var used = 0.0;
            visible = 0;
            for (var i = 0; i < count; i++)
            {
                _widths[i] = Math.Min(_widths[i], MinimumTabWidth);
                if (used + _widths[i] <= available || visible == 0)
                {
                    used += _widths[i];
                    visible++;
                }
            }
        }

        MarkOverflow(visible, active >= visible ? active : -1);
        MeasureAt(availableSize.Height);
        return new Size(Math.Min(available, total), height);
    }

    private void MeasureAt(double height)
    {
        for (var i = 0; i < Children.Count; i++)
        {
            if (Children[i] is not DockTab { IsOverflowed: true })
                Children[i].Measure(new Size(_widths[i], height));
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (child is DockTab { IsOverflowed: true })
            {
                child.Arrange(new Rect(-10_000, 0, 0, 0));
                continue;
            }

            var width = i < _widths.Length ? _widths[i] : child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    private void MarkOverflow(int visible, int promoted)
    {
        var overflowing = false;
        for (var i = 0; i < Children.Count; i++)
        {
            if (Children[i] is not DockTab tab)
                continue;
            var hidden = promoted >= 0
                ? i >= visible - 1 && i != promoted
                : i >= visible;
            tab.IsOverflowed = hidden;
            overflowing |= hidden;
        }

        IsOverflowing = overflowing;
    }
}
