using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Talesmith.UI.Controls;

/// <summary>Where a dragged item lands relative to the item under the pointer.</summary>
public enum DropPosition
{
    Before,
    Inside,
    After
}

/// <summary>Shows where a drag-and-drop will insert, as a line before or after an item or an outline around it, for trees, lists and grids.</summary>
/// <example>
/// <code>
/// var position = DropIndicator.PositionAt(item, e.GetPosition(item), canDropInside: node.IsFolder);
/// DropIndicator.Show(item, position, indent: depth * 14);
/// // on drop or drag leave:
/// DropIndicator.Hide(item);
/// </code>
/// </example>
public static class DropIndicator
{
    private static readonly AttachedProperty<DropIndicatorAdorner?> AdornerProperty =
        AvaloniaProperty.RegisterAttached<Control, DropIndicatorAdorner?>("Adorner", typeof(DropIndicator));

    /// <summary>Gets the drop position for a pointer at <paramref name="offset"/> along an item of <paramref name="extent"/>: the outer quarters
    /// insert before or after, the middle drops inside; without inside drops the halves decide.</summary>
    public static DropPosition PositionFromOffset(double offset, double extent, bool canDropInside)
    {
        if (extent <= 0)
            return DropPosition.After;
        var fraction = offset / extent;
        if (!canDropInside)
            return fraction < 0.5 ? DropPosition.Before : DropPosition.After;
        return fraction < 0.25 ? DropPosition.Before : fraction > 0.75 ? DropPosition.After : DropPosition.Inside;
    }

    /// <summary>Gets the drop position for a pointer at <paramref name="point"/> within <paramref name="target"/>.</summary>
    public static DropPosition PositionAt(Control target, Point point, bool canDropInside = true, Orientation orientation = Orientation.Vertical) =>
        orientation == Orientation.Vertical
            ? PositionFromOffset(point.Y, target.Bounds.Height, canDropInside)
            : PositionFromOffset(point.X, target.Bounds.Width, canDropInside);

    /// <summary>Shows the indicator on <paramref name="target"/>, replacing any shown before.</summary>
    /// <param name="indent">How far the insertion line starts from the item's leading edge, such as the depth of a tree item.</param>
    /// <param name="orientation">Vertical for lists and trees, horizontal for items laid out in a row.</param>
    public static void Show(Control target, DropPosition position, double indent = 0, Orientation orientation = Orientation.Vertical)
    {
        ArgumentNullException.ThrowIfNull(target);
        var adorner = target.GetValue(AdornerProperty);
        if (adorner is null)
        {
            if (AdornerLayer.GetAdornerLayer(target) is null)
                return;
            adorner = new DropIndicatorAdorner();
            target.SetValue(AdornerProperty, adorner);
            AdornerLayer.SetAdorner(target, adorner);
        }

        adorner.Update(target, position, indent, orientation);
    }

    /// <summary>Removes the indicator from <paramref name="target"/>.</summary>
    public static void Hide(Control target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.GetValue(AdornerProperty) is null)
            return;
        AdornerLayer.SetAdorner(target, null);
        target.ClearValue(AdornerProperty);
    }

    private sealed class DropIndicatorAdorner : Control
    {
        private IBrush _accent = Brushes.CornflowerBlue;
        private IBrush _fill = Brushes.Transparent;
        private DropPosition _position;
        private double _indent;
        private Orientation _orientation;

        public DropIndicatorAdorner()
        {
            IsHitTestVisible = false;
        }

        public void Update(Control target, DropPosition position, double indent, Orientation orientation)
        {
            if (target.TryFindResource("AccentColor", target.ActualThemeVariant, out var value) && value is Color accent)
            {
                _accent = new ImmutableSolidColorBrush(accent);
                _fill = new ImmutableSolidColorBrush(accent, 0.14);
            }

            _position = position;
            _indent = indent;
            _orientation = orientation;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            if (_position == DropPosition.Inside)
            {
                context.DrawRectangle(_fill, new Pen(_accent, 1.5), new RoundedRect(bounds.Deflate(1), 6));
                return;
            }

            const double thickness = 2;
            const double radius = 3.5;
            var pen = new Pen(_accent, thickness, lineCap: PenLineCap.Round);
            if (_orientation == Orientation.Vertical)
            {
                var y = _position == DropPosition.Before ? 1 : bounds.Height - 1;
                var start = new Point(_indent + radius * 2, y);
                context.DrawLine(pen, start, new Point(bounds.Width - 2, y));
                context.DrawEllipse(null, new Pen(_accent, 1.5), new Point(_indent + radius, y), radius, radius);
            }
            else
            {
                var x = _position == DropPosition.Before ? 1 : bounds.Width - 1;
                context.DrawLine(pen, new Point(x, _indent + 2), new Point(x, bounds.Height - 2));
            }
        }
    }
}
