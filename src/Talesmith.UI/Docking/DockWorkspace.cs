using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Talesmith.UI.Docking;

/// <summary>The handle between two neighbors of a <see cref="DockSplit"/>: a 1px line with a wider grab area.</summary>
[PseudoClasses(":horizontal", ":vertical", ":dragging")]
public class DockSplitter : TemplatedControl
{
    internal DockSplitter(DockSplit split, DockNode leading)
    {
        Split = split;
        Leading = leading;
        var horizontal = split.Orientation == DockOrientation.Horizontal;
        PseudoClasses.Set(":horizontal", horizontal);
        PseudoClasses.Set(":vertical", !horizontal);
        Cursor = new Cursor(horizontal ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
    }

    internal DockSplit Split { get; }

    /// <summary>Gets the shown child before the splitter.</summary>
    internal DockNode Leading { get; }

    internal bool IsDragging
    {
        set => PseudoClasses.Set(":dragging", value);
    }
}

/// <summary>Lays out the group views of a dock tree side by side in one panel, as the tree's splits divide the space, with a splitter between
/// shown neighbors.</summary>
/// <remarks>
/// A change of the tree places the views again rather than rebuilding them, so a panel that stays in its group keeps its place in the visual
/// tree. Every group keeps at least <see cref="MinimumGroupSize"/> while the space allows, and dragging a splitter stops there; when the
/// space is too small for every group's minimum, all groups shrink alike.
/// </remarks>
internal sealed class DockWorkspace : Panel
{
    /// <summary>The thickness of the visible line between neighbors.</summary>
    public const double Gap = 1;

    /// <summary>The smallest size a group keeps: room for its tab strip, a tab and some of its content.</summary>
    public static readonly Size MinimumGroupSize = new(140, 80);

    private const double GrabArea = 7;

    private readonly Dictionary<(DockSplit Split, DockNode Leading), DockSplitter> _splitters = [];
    private readonly Dictionary<DockNode, Rect> _nodes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Control, Rect> _places = new(ReferenceEqualityComparer.Instance);
    private DockLayout? _layout;
    private Func<DockGroup, Control>? _viewOf;
    private DockNode? _root;
    private DockGroup? _maximized;
    private Control? _empty;
    private SplitterDrag? _drag;

    public DockWorkspace() => ClipToBounds = true;

    /// <summary>Shows a tree of <paramref name="layout"/>: its groups' views, splitters between shown neighbors, only
    /// <paramref name="maximized"/> when set, and <paramref name="empty"/> when the tree has no panels.</summary>
    public void Show(DockLayout layout, DockNode root, DockGroup? maximized, Func<DockGroup, Control> viewOf, Control empty)
    {
        _layout = layout;
        _root = root;
        _maximized = maximized;
        _viewOf = viewOf;
        _empty = empty;

        var wanted = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var splitters = new HashSet<(DockSplit, DockNode)>();
        if (root is DockGroup { Panels.Count: 0 })
            wanted.Add(empty);
        else
            Collect(root, wanted, splitters);

        foreach (var key in _splitters.Keys.Where(k => !splitters.Contains(k)).ToList())
        {
            Children.Remove(_splitters[key]);
            _splitters.Remove(key);
        }

        foreach (var key in splitters)
        {
            if (_splitters.ContainsKey(key))
                continue;
            var splitter = new DockSplitter(key.Item1, key.Item2);
            splitter.PointerPressed += OnSplitterPressed;
            splitter.PointerMoved += OnSplitterMoved;
            splitter.PointerReleased += OnSplitterReleased;
            splitter.PointerCaptureLost += (_, _) => EndDrag();
            _splitters[key] = splitter;
            wanted.Add(splitter);
        }

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(Children[i]) && Children[i] is not DockSplitter)
                Children.RemoveAt(i);
        }

        foreach (var control in wanted)
        {
            if (!Children.Contains(control))
                Children.Add(control);
        }

        foreach (var child in Children)
            child.IsVisible = maximized is null || ReferenceEquals(child, viewOf(maximized));
        InvalidateMeasure();
    }

    /// <summary>The bounds of a node at the last layout, in this panel's coordinates.</summary>
    public Rect? BoundsOf(DockNode node) => _nodes.TryGetValue(node, out var bounds) ? bounds : null;

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        Place(size);
        foreach (var child in Children)
            child.Measure(_places.TryGetValue(child, out var place) ? place.Size : default);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize);
        foreach (var child in Children)
            child.Arrange(_places.TryGetValue(child, out var place) ? place : default);
        return finalSize;
    }

    /// <summary>The smallest size a node fits in while each group keeps its minimum.</summary>
    public static Size Minimum(DockNode node)
    {
        if (node is DockGroup)
            return MinimumGroupSize;
        var split = (DockSplit)node;
        var shown = split.Children.Where(c => !c.IsCollapsed).Select(Minimum).ToList();
        if (shown.Count == 0)
            return default;
        var gaps = Gap * (shown.Count - 1);
        return split.Orientation == DockOrientation.Horizontal
            ? new Size(shown.Sum(s => s.Width) + gaps, shown.Max(s => s.Height))
            : new Size(shown.Max(s => s.Width), shown.Sum(s => s.Height) + gaps);
    }

    /// <summary>Divides <paramref name="extent"/> by <paramref name="weights"/>, giving each part at least its minimum when there is room for every
    /// minimum, and shrinking all minimums alike when there is not; parts are whole pixels that add up to the extent.</summary>
    public static double[] Distribute(double extent, IReadOnlyList<double> weights, IReadOnlyList<double> minimums)
    {
        var count = weights.Count;
        var lengths = new double[count];
        if (count == 0 || extent <= 0)
            return lengths;
        var least = minimums.Sum();
        if (extent <= least)
        {
            for (var i = 0; i < count; i++)
                lengths[i] = least > 0 ? extent * minimums[i] / least : extent / count;
        }
        else
        {
            var fixedAtMinimum = new bool[count];
            while (true)
            {
                var free = extent;
                var weight = 0.0;
                for (var i = 0; i < count; i++)
                {
                    if (fixedAtMinimum[i])
                        free -= minimums[i];
                    else
                        weight += Math.Max(weights[i], 1e-6);
                }

                var changed = false;
                for (var i = 0; i < count; i++)
                {
                    if (fixedAtMinimum[i])
                    {
                        lengths[i] = minimums[i];
                        continue;
                    }

                    lengths[i] = free * Math.Max(weights[i], 1e-6) / weight;
                    if (lengths[i] < minimums[i])
                    {
                        fixedAtMinimum[i] = true;
                        changed = true;
                    }
                }

                if (!changed)
                    break;
            }
        }

        var assigned = 0.0;
        for (var i = 0; i < count - 1; i++)
        {
            lengths[i] = Math.Round(lengths[i]);
            assigned += lengths[i];
        }

        lengths[^1] = Math.Max(0, extent - assigned);
        return lengths;
    }

    private void Place(Size size)
    {
        _nodes.Clear();
        _places.Clear();
        if (_root is null || _viewOf is null)
            return;
        var area = new Rect(size);
        if (_root is DockGroup { Panels.Count: 0 })
        {
            if (_empty is not null)
                _places[_empty] = area;
            return;
        }

        if (_maximized is not null)
        {
            _nodes[_maximized] = area;
            _places[_viewOf(_maximized)] = area;
            return;
        }

        Place(_root, area);
    }

    private void Place(DockNode node, Rect area)
    {
        _nodes[node] = area;
        if (node is DockGroup group)
        {
            _places[_viewOf!(group)] = area;
            return;
        }

        var split = (DockSplit)node;
        var horizontal = split.Orientation == DockOrientation.Horizontal;
        var shown = split.Children.Where(c => !c.IsCollapsed).ToList();
        if (shown.Count == 0)
            return;
        var extent = (horizontal ? area.Width : area.Height) - Gap * (shown.Count - 1);
        var lengths = Distribute(extent, [.. shown.Select(c => c.Size)],
            [.. shown.Select(c => horizontal ? Minimum(c).Width : Minimum(c).Height)]);
        var offset = horizontal ? area.X : area.Y;
        for (var i = 0; i < shown.Count; i++)
        {
            if (i > 0)
            {
                if (_splitters.TryGetValue((split, shown[i - 1]), out var splitter))
                {
                    var grab = offset - (GrabArea - Gap) / 2;
                    _places[splitter] = horizontal ? new Rect(grab, area.Y, GrabArea, area.Height) : new Rect(area.X, grab, area.Width, GrabArea);
                }

                offset += Gap;
            }

            Place(shown[i], horizontal ? new Rect(offset, area.Y, lengths[i], area.Height) : new Rect(area.X, offset, area.Width, lengths[i]));
            offset += lengths[i];
        }
    }

    private void Collect(DockNode node, HashSet<Control> views, HashSet<(DockSplit, DockNode)> splitters)
    {
        if (node is DockGroup group)
        {
            views.Add(_viewOf!(group));
            return;
        }

        var split = (DockSplit)node;
        DockNode? previous = null;
        foreach (var child in split.Children)
        {
            Collect(child, views, splitters);
            if (child.IsCollapsed)
                continue;
            if (previous is not null)
                splitters.Add((split, previous));
            previous = child;
        }
    }

    private void OnSplitterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DockSplitter splitter || _layout is null || !e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            return;
        var split = splitter.Split;
        var leading = splitter.Leading;
        var trailing = split.Children.Skip(split.Children.IndexOf(leading) + 1).FirstOrDefault(c => !c.IsCollapsed);
        if (trailing is null || BoundsOf(leading) is not { } before || BoundsOf(trailing) is not { } after)
            return;

        var horizontal = split.Orientation == DockOrientation.Horizontal;
        if (e.ClickCount == 2)
        {
            var total = leading.Size + trailing.Size;
            _layout.Resize(split.Id, split.Children.IndexOf(leading), split.Children.IndexOf(trailing), total / 2, total / 2);
            e.Handled = true;
            return;
        }

        _drag = new SplitterDrag(splitter, trailing, Coordinate(e.GetPosition(this), horizontal), horizontal ? before.Width : before.Height,
            horizontal ? after.Width : after.Height);
        splitter.IsDragging = true;
        e.Pointer.Capture(splitter);
        e.Handled = true;
    }

    private void OnSplitterMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } drag || _layout is null || !ReferenceEquals(sender, drag.Splitter))
            return;
        var split = drag.Splitter.Split;
        var leading = drag.Splitter.Leading;
        var horizontal = split.Orientation == DockOrientation.Horizontal;
        var pixels = drag.LeadingLength + drag.NextLength;
        var leadingMinimum = Math.Min(horizontal ? Minimum(leading).Width : Minimum(leading).Height, pixels / 2);
        var nextMinimum = Math.Min(horizontal ? Minimum(drag.Next).Width : Minimum(drag.Next).Height, pixels / 2);
        var length = Math.Clamp(drag.LeadingLength + Coordinate(e.GetPosition(this), horizontal) - drag.Start, leadingMinimum, pixels - nextMinimum);
        var weight = leading.Size + drag.Next.Size;
        _layout.Resize(split.Id, split.Children.IndexOf(leading), split.Children.IndexOf(drag.Next), weight * length / pixels,
            weight * (pixels - length) / pixels);
        e.Handled = true;
    }

    private void OnSplitterReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is null)
            return;
        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        _drag?.Splitter.IsDragging = false;
        _drag = null;
    }

    private static double Coordinate(Point point, bool horizontal) => horizontal ? point.X : point.Y;

    private sealed record SplitterDrag(DockSplitter Splitter, DockNode Next, double Start, double LeadingLength, double NextLength);
}
