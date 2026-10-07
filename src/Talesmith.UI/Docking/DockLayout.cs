using Avalonia;

namespace Talesmith.UI.Docking;

/// <summary>What a <see cref="DockLayout"/> change affects, so views can update as little as possible.</summary>
public enum DockChangeKind
{
    /// <summary>Nodes were added, removed, moved, collapsed or maximized, or floating windows opened or closed.</summary>
    Structure,

    /// <summary>The active panel of a group or the focused group changed.</summary>
    Activation,

    /// <summary>Only the sizes of split children changed.</summary>
    Sizes,

    /// <summary>Only where a floating window is, or its size, changed.</summary>
    Bounds
}

public sealed class DockLayoutChangedEventArgs(DockChangeKind kind) : EventArgs
{
    public DockChangeKind Kind { get; } = kind;
}

/// <summary>Where a closed panel was, so it can reopen in the same place.</summary>
/// <param name="GroupId">The group the panel was in.</param>
/// <param name="Index">The panel's tab index in that group.</param>
/// <param name="NeighborId">The node next to the group, when the group was removed with the panel.</param>
/// <param name="Edge">The side of the neighbor the group was on.</param>
/// <param name="Fraction">The share of the neighbor's space the group took.</param>
public sealed record DockPlacement(string GroupId, int Index, string? NeighborId = null, DockEdge Edge = DockEdge.Right, double Fraction = 0.5);

/// <summary>The arrangement of a dock workspace: a tree of splits whose leaves are tab groups of panel ids, and floating windows with trees of
/// their own.</summary>
/// <remarks>
/// The layout holds ids only. Groups that lose their last panel are removed, splits left with one child are merged into their parent and
/// floating windows left without panels close. A panel that leaves the workspace for a floating window remembers where it was, and returns
/// there when its window closes.
/// </remarks>
public sealed class DockLayout
{
    private readonly Dictionary<string, DockPlacement> _closed = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DockPlacement> _homes = new(StringComparer.Ordinal);
    private readonly List<DockFloat> _floats = [];
    private DockGroup? _focused;
    private int _batching;
    private DockChangeKind? _pending;

    public DockLayout(DockNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
        root.Parent = null;
        Normalize();
        _focused = Groups.FirstOrDefault();
    }

    /// <summary>Raised after every change.</summary>
    public event EventHandler<DockLayoutChangedEventArgs>? Changed;

    /// <summary>Gets the root of the workspace, the tree of the main window.</summary>
    public DockNode Root { get; private set; }

    /// <summary>Gets the group the user last worked in; its active tab is highlighted.</summary>
    public DockGroup? FocusedGroup => _focused;

    /// <summary>Gets the group filling its window, or null.</summary>
    public DockGroup? MaximizedGroup { get; private set; }

    /// <summary>Gets or sets the layout consulted when a panel that was never placed must be shown, typically the default layout.</summary>
    public DockLayout? Fallback { get; set; }

    /// <summary>Gets the floating windows, in the order they opened.</summary>
    public IReadOnlyList<DockFloat> Floats => _floats;

    /// <summary>Gets every node of the workspace and of the floating windows, depth first.</summary>
    public IEnumerable<DockNode> Nodes => Roots.SelectMany(Descendants);

    public IEnumerable<DockGroup> Groups => Nodes.OfType<DockGroup>();

    /// <summary>Gets the ids of every panel in the layout.</summary>
    public IEnumerable<string> Panels => Groups.SelectMany(g => g.Panels);

    /// <summary>Gets where closed panels were, keyed by panel id.</summary>
    public IReadOnlyDictionary<string, DockPlacement> ClosedPanels => _closed;

    /// <summary>Gets where the panels in floating windows were in the workspace, keyed by panel id.</summary>
    public IReadOnlyDictionary<string, DockPlacement> FloatingPanelHomes => _homes;

    private IEnumerable<DockNode> Roots => [Root, .. _floats.Select(f => f.Root)];

    public DockNode? FindNode(string id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>Gets the group containing <paramref name="panelId"/>, or null when the panel is not in the layout.</summary>
    public DockGroup? FindPanel(string panelId) => Groups.FirstOrDefault(g => g.Panels.Contains(panelId));

    public bool Contains(string panelId) => FindPanel(panelId) is not null;

    public DockFloat? FindFloat(string floatId) => _floats.Find(f => f.Id == floatId);

    /// <summary>Gets the floating window holding <paramref name="node"/>, or null when the node is in the workspace.</summary>
    public DockFloat? FloatOf(DockNode node)
    {
        var top = TopOf(node);
        return _floats.Find(f => ReferenceEquals(f.Root, top));
    }

    /// <summary>Gets whether <paramref name="node"/> and all its ancestors are expanded, and no other group of its window is maximized.</summary>
    public bool IsShown(DockNode node)
    {
        for (DockNode? current = node; current is not null; current = current.Parent)
        {
            if (current.IsCollapsed)
                return false;
        }

        return MaximizedGroup is null || ReferenceEquals(MaximizedGroup, node) || IsAncestor(node, MaximizedGroup)
            || !ReferenceEquals(TopOf(MaximizedGroup), TopOf(node));
    }

    /// <summary>Shows <paramref name="panelId"/> in its group and focuses the group.</summary>
    public bool ActivatePanel(string panelId)
    {
        if (FindPanel(panelId) is not { } group)
            return false;
        if (group.ActivePanel == panelId && ReferenceEquals(_focused, group))
            return true;
        group.ActivePanel = panelId;
        _focused = group;
        Raise(DockChangeKind.Activation);
        return true;
    }

    public bool FocusGroup(string groupId)
    {
        if (FindNode(groupId) is not DockGroup group)
            return false;
        if (!ReferenceEquals(_focused, group))
        {
            _focused = group;
            Raise(DockChangeKind.Activation);
        }

        return true;
    }

    /// <summary>Moves or adds <paramref name="panelId"/> as a tab of a group.</summary>
    /// <param name="index">The insertion position among the group's current tabs; -1 appends.</param>
    public bool MovePanel(string panelId, string groupId, int index = -1)
    {
        if (FindNode(groupId) is not DockGroup target)
            return false;

        var source = FindPanel(panelId);
        if (ReferenceEquals(source, target))
        {
            var current = target.Panels.IndexOf(panelId);
            if (index > current)
                index--;
            if (index < 0)
                index = target.Panels.Count - 1;
            target.Insert(index, panelId);
        }
        else
        {
            Detach(panelId, intoFloat: FloatOf(target) is not null);
            target.Insert(index, panelId);
            _closed.Remove(panelId);
            Normalize();
        }

        target.ActivePanel = panelId;
        _focused = target;
        Raise(DockChangeKind.Structure);
        return true;
    }

    /// <summary>Moves or adds <paramref name="panelId"/> into a new group beside a node.</summary>
    /// <param name="targetId">The node to split; the root id docks along the edge of the whole workspace.</param>
    /// <param name="fraction">The share of the target's space the new group takes.</param>
    public bool DockPanel(string panelId, string targetId, DockEdge edge, double fraction = 0.5) =>
        DockPanel(panelId, targetId, edge, fraction, null);

    /// <summary>Removes <paramref name="panelId"/> and remembers where it was.</summary>
    public bool ClosePanel(string panelId)
    {
        if (FindPanel(panelId) is not { } group)
            return false;

        _closed[panelId] = PlacementOf(group, panelId);
        group.Remove(panelId);
        Normalize();
        Raise(DockChangeKind.Structure);
        return true;
    }

    /// <summary>Gets whether <see cref="FloatPanel"/> can move a panel: it is in the layout, and not the last panel of the workspace.</summary>
    public bool CanFloat(string panelId) =>
        FindPanel(panelId) is { } group && (FloatOf(group) is not null || WorkspaceGroups.Sum(g => g.Panels.Count) > 1);

    /// <summary>Moves a panel into a new floating window. The panel returns to its place in the workspace when the window closes.</summary>
    /// <param name="position">The window's top-left corner on screen, in device pixels.</param>
    /// <param name="size">The size of the window's content.</param>
    /// <returns>The new window, or null when the panel is not in the layout or is the last panel of the workspace, which keeps it.</returns>
    public DockFloat? FloatPanel(string panelId, PixelPoint position, Size size)
    {
        if (!CanFloat(panelId))
            return null;

        Detach(panelId, intoFloat: true);
        var group = new DockGroup(null, panelId);
        var window = new DockFloat(null, group, position, Valid(size));
        _floats.Add(window);
        Normalize();
        _focused = group;
        Raise(DockChangeKind.Structure);
        return window;
    }

    /// <summary>Moves a panel from a floating window back to the workspace, where it was before it left.</summary>
    public bool ReturnPanel(string panelId)
    {
        if (FindPanel(panelId) is not { } group || FloatOf(group) is null)
            return false;

        _homes.Remove(panelId, out var home);
        Batch(() =>
        {
            group.Remove(panelId);
            Normalize();
            PlaceInWorkspace(panelId, home);
        });
        return true;
    }

    /// <summary>Closes a floating window, returning each of its panels to where it was in the workspace.</summary>
    public bool CloseFloat(string floatId)
    {
        if (FindFloat(floatId) is not { } window)
            return false;

        var groups = Descendants(window.Root).OfType<DockGroup>().ToList();
        var active = groups.Select(g => g.ActivePanel).OfType<string>().ToList();
        var pending = groups.SelectMany(g => g.Panels).ToList();
        Batch(() =>
        {
            // A panel whose group is gone can only rejoin its old tabs once the panel that recreates the group is back.
            while (pending.Count > 0)
            {
                var next = Math.Max(0, pending.FindIndex(CanReturnHome));
                ReturnPanel(pending[next]);
                pending.RemoveAt(next);
            }

            foreach (var panel in active)
                ActivatePanel(panel);
        });
        return true;
    }

    /// <summary>Records where a floating window is and its size, as moving or resizing the window does.</summary>
    public bool MoveFloat(string floatId, PixelPoint position, Size size)
    {
        if (FindFloat(floatId) is not { } window)
            return false;

        size = Valid(size);
        if (window.Position == position && window.Size == size)
            return true;
        window.Position = position;
        window.Size = size;
        Raise(DockChangeKind.Bounds);
        return true;
    }

    /// <summary>Makes <paramref name="panelId"/> visible: reopens it where it was last, or where the fallback layout puts it, expands collapsed
    /// ancestors, leaves a maximized group and activates it.</summary>
    /// <remarks>Raises <see cref="DockChangeKind.Structure"/> only when the arrangement changed; a panel that only needs its tab activated
    /// raises <see cref="DockChangeKind.Activation"/>, and one already showing raises nothing.</remarks>
    public void EnsureVisible(string panelId)
    {
        if (!Contains(panelId))
            Reopen(panelId);

        var group = FindPanel(panelId)!;
        var restructured = false;
        for (DockNode? node = group; node is not null; node = node.Parent)
        {
            restructured |= node.IsCollapsed;
            node.IsCollapsed = false;
        }

        if (MaximizedGroup is not null && !ReferenceEquals(MaximizedGroup, group) && ReferenceEquals(TopOf(MaximizedGroup), TopOf(group)))
        {
            MaximizedGroup = null;
            restructured = true;
        }

        if (!restructured)
        {
            ActivatePanel(panelId);
            return;
        }

        group.ActivePanel = panelId;
        _focused = group;
        Raise(DockChangeKind.Structure);
    }

    /// <summary>Fills the workspace with a group, or restores the layout when it is already maximized.</summary>
    public bool ToggleMaximize(string groupId)
    {
        if (FindNode(groupId) is not DockGroup group)
            return false;
        MaximizedGroup = ReferenceEquals(MaximizedGroup, group) ? null : group;
        _focused = group;
        Raise(DockChangeKind.Structure);
        return true;
    }

    /// <summary>Leaves the maximized state.</summary>
    public void Restore()
    {
        if (MaximizedGroup is null)
            return;
        MaximizedGroup = null;
        Raise(DockChangeKind.Structure);
    }

    /// <summary>Hides or shows a node, such as a side region; its siblings take its space while it is hidden.</summary>
    public bool SetCollapsed(string nodeId, bool collapsed)
    {
        if (FindNode(nodeId) is not { } node)
            return false;
        if (node.IsCollapsed == collapsed)
            return true;
        node.IsCollapsed = collapsed;
        if (collapsed && MaximizedGroup is not null && (ReferenceEquals(node, MaximizedGroup) || IsAncestor(MaximizedGroup, node)))
            MaximizedGroup = null;
        Raise(DockChangeKind.Structure);
        return true;
    }

    public bool ToggleCollapsed(string nodeId) => FindNode(nodeId) is { } node && SetCollapsed(nodeId, !node.IsCollapsed);

    /// <summary>Sets the sizes of two neighbouring children of a split, as dragging the splitter between them does.</summary>
    public bool Resize(string splitId, int index, double leading, double trailing) => Resize(splitId, index, index + 1, leading, trailing);

    /// <summary>Sets the sizes of two children of a split, such as the children either side of a collapsed one.</summary>
    public bool Resize(string splitId, int leadingIndex, int trailingIndex, double leading, double trailing)
    {
        if (FindNode(splitId) is not DockSplit split || leadingIndex < 0 || trailingIndex >= split.Children.Count || leadingIndex >= trailingIndex)
            return false;
        split.Children[leadingIndex].SetSize(leading);
        split.Children[trailingIndex].SetSize(trailing);
        Raise(DockChangeKind.Sizes);
        return true;
    }

    /// <summary>Adds a floating window read from a saved layout; one without panels is left out.</summary>
    internal void AddFloat(DockFloat window)
    {
        if (NormalizeNode(window.Root) is not { } root)
            return;
        root.Parent = null;
        window.Root = root;
        window.Size = Valid(window.Size);
        _floats.Add(window);
    }

    internal void RestoreState(DockGroup? focused, DockGroup? maximized, IEnumerable<KeyValuePair<string, DockPlacement>> closed,
        IEnumerable<KeyValuePair<string, DockPlacement>> homes)
    {
        _focused = focused ?? _focused;
        MaximizedGroup = maximized;
        foreach (var (panel, placement) in closed)
        {
            if (!Contains(panel))
                _closed[panel] = placement;
        }

        foreach (var (panel, placement) in homes)
        {
            if (FindPanel(panel) is not { } group || FloatOf(group) is not null)
                _homes[panel] = placement;
        }
    }

    private bool DockPanel(string panelId, string targetId, DockEdge edge, double fraction, string? groupId)
    {
        if (FindNode(targetId) is not { } target)
            return false;

        var source = FindPanel(panelId);
        if (source is not null && ReferenceEquals(source, target) && source.Panels.Count == 1)
            return false;

        if (groupId is not null && FindNode(groupId) is not null)
            groupId = null;
        var group = new DockGroup(groupId, panelId);
        Detach(panelId, intoFloat: FloatOf(target) is not null);
        InsertBeside(target, edge, group, Math.Clamp(fraction, 0.05, 0.95));
        for (DockNode? node = target; node is not null; node = node.Parent)
            node.IsCollapsed = false;
        _closed.Remove(panelId);
        if (MaximizedGroup is not null && ReferenceEquals(TopOf(MaximizedGroup), TopOf(group)))
            MaximizedGroup = null;
        Normalize();
        _focused = group;
        Raise(DockChangeKind.Structure);
        return true;
    }

    private void InsertBeside(DockNode target, DockEdge edge, DockNode node, double fraction)
    {
        var orientation = edge is DockEdge.Left or DockEdge.Right ? DockOrientation.Horizontal : DockOrientation.Vertical;
        var before = edge is DockEdge.Left or DockEdge.Top;

        if (target.Parent is { } parent && parent.Orientation == orientation)
        {
            node.SetSize(target.Size * fraction);
            target.SetSize(target.Size * (1 - fraction));
            parent.Insert(parent.Children.IndexOf(target) + (before ? 0 : 1), node);
            return;
        }

        var size = target.Size;
        var container = target.Parent;
        var split = new DockSplit(null, orientation) { Size = size };
        if (container is null)
            ReplaceRoot(target, split);
        else
            container.Replace(target, split);

        target.SetSize(1 - fraction);
        node.SetSize(fraction);
        split.Insert(0, before ? node : target);
        split.Insert(1, before ? target : node);
    }

    private void Reopen(string panelId)
    {
        if (_closed.TryGetValue(panelId, out var placement))
        {
            if (FindNode(placement.GroupId) is DockGroup group)
            {
                MovePanel(panelId, group.Id, placement.Index);
                return;
            }

            if (placement.NeighborId is { } neighbor && DockPanel(panelId, neighbor, placement.Edge, placement.Fraction, placement.GroupId))
                return;
        }

        PlaceInWorkspace(panelId, _homes.GetValueOrDefault(panelId));
    }

    /// <summary>Puts a panel that is in no group into the workspace: where <paramref name="placement"/> says, else where the fallback layout has
    /// it, else in the focused group of the workspace.</summary>
    private void PlaceInWorkspace(string panelId, DockPlacement? placement)
    {
        if (placement is not null)
        {
            if (FindWorkspaceNode(placement.GroupId) is DockGroup group)
            {
                MovePanel(panelId, group.Id, placement.Index);
                return;
            }

            if (placement.NeighborId is { } neighbor && FindWorkspaceNode(neighbor) is not null
                && DockPanel(panelId, neighbor, placement.Edge, placement.Fraction, placement.GroupId))
            {
                return;
            }
        }

        if (Fallback?.FindPanel(panelId) is { } preferred && PlaceLike(panelId, preferred))
            return;

        var fallback = _focused is { } focused && FloatOf(focused) is null ? focused : WorkspaceGroups.First();
        MovePanel(panelId, fallback.Id);
    }

    private bool PlaceLike(string panelId, DockGroup preferred)
    {
        if (FindWorkspaceNode(preferred.Id) is DockGroup existing)
            return MovePanel(panelId, existing.Id, preferred.Panels.Count(p => Contains(p) && preferred.Panels.IndexOf(p) < preferred.Panels.IndexOf(panelId)));

        DockNode node = preferred;
        while (node.Parent is { } parent)
        {
            var index = parent.Children.IndexOf(node);
            var siblings = parent.Children
                .Select((child, i) => (child, i))
                .Where(s => s.i != index)
                .OrderBy(s => Math.Abs(s.i - index));
            foreach (var (sibling, i) in siblings)
            {
                var target = FindWorkspaceNode(sibling.Id)
                    ?? Descendants(sibling).OfType<DockGroup>().Select(g => FindWorkspaceNode(g.Id)).FirstOrDefault(n => n is not null);
                if (target is null)
                    continue;
                var after = i < index;
                var edge = parent.Orientation == DockOrientation.Horizontal
                    ? after ? DockEdge.Right : DockEdge.Left
                    : after ? DockEdge.Bottom : DockEdge.Top;
                return DockPanel(panelId, target.Id, edge, node.Size / (node.Size + sibling.Size), preferred.Id);
            }

            node = parent;
        }

        return false;
    }

    private void Normalize()
    {
        var root = NormalizeNode(Root) ?? (Root as DockGroup ?? new DockGroup(null));
        root.Parent = null;
        Root = root;

        for (var i = _floats.Count - 1; i >= 0; i--)
        {
            if (NormalizeNode(_floats[i].Root) is { } floating)
            {
                floating.Parent = null;
                _floats[i].Root = floating;
            }
            else
            {
                _floats.RemoveAt(i);
            }
        }

        if (_focused is null || !IsAttached(_focused))
            _focused = Groups.FirstOrDefault();
        if (MaximizedGroup is not null && !IsAttached(MaximizedGroup))
            MaximizedGroup = null;
    }

    private static DockNode? NormalizeNode(DockNode node)
    {
        if (node is DockGroup group)
            return group.Panels.Count == 0 ? null : group;

        var split = (DockSplit)node;
        foreach (var child in split.Children.ToList())
        {
            var normalized = NormalizeNode(child);
            if (normalized is null)
            {
                split.Remove(child);
            }
            else if (!ReferenceEquals(normalized, child))
            {
                normalized.SetSize(child.Size);
                normalized.IsCollapsed |= child.IsCollapsed;
                split.Replace(child, normalized);
            }
        }

        foreach (var child in split.Children.ToList())
        {
            if (child is not DockSplit nested || nested.Orientation != split.Orientation || nested.IsCollapsed)
                continue;
            var total = nested.Children.Sum(c => c.Size);
            var index = split.Children.IndexOf(nested);
            split.Remove(nested);
            foreach (var grandchild in nested.Children.ToList())
            {
                nested.Remove(grandchild);
                grandchild.SetSize(grandchild.Size / total * nested.Size);
                split.Insert(index++, grandchild);
            }
        }

        if (split.Children.Count == 0)
            return null;
        if (split.Children.Count == 1)
        {
            var only = split.Children[0];
            split.Remove(only);
            return only;
        }

        var sum = split.Children.Sum(c => c.Size);
        foreach (var child in split.Children)
            child.SetSize(child.Size / sum);
        return split;
    }

    private bool IsAttached(DockNode node)
    {
        var top = TopOf(node);
        return ReferenceEquals(top, Root) || _floats.Exists(f => ReferenceEquals(f.Root, top));
    }

    private static DockNode TopOf(DockNode node)
    {
        var top = node;
        while (top.Parent is { } parent)
            top = parent;
        return top;
    }

    private IEnumerable<DockGroup> WorkspaceGroups => Descendants(Root).OfType<DockGroup>();

    private bool CanReturnHome(string panelId) => _homes.TryGetValue(panelId, out var home)
        && (FindWorkspaceNode(home.GroupId) is DockGroup || (home.NeighborId is { } neighbor && FindWorkspaceNode(neighbor) is not null));

    private DockNode? FindWorkspaceNode(string id) => Descendants(Root).FirstOrDefault(n => n.Id == id);

    private void ReplaceRoot(DockNode root, DockNode replacement)
    {
        if (ReferenceEquals(root, Root))
            Root = replacement;
        else if (_floats.Find(f => ReferenceEquals(f.Root, root)) is { } window)
            window.Root = replacement;
    }

    /// <summary>Takes a panel out of its group before it moves, remembering where it was in the workspace when it leaves for a floating window,
    /// and forgetting that once it moves back.</summary>
    private void Detach(string panelId, bool intoFloat)
    {
        if (FindPanel(panelId) is { } group)
        {
            if (intoFloat && FloatOf(group) is null)
                _homes[panelId] = PlacementOf(group, panelId);
            group.Remove(panelId);
        }

        if (!intoFloat)
            _homes.Remove(panelId);
    }

    /// <summary>Where a panel is: its group and tab index, and when it is the group's only panel, the neighbor to dock the group beside again.</summary>
    private static DockPlacement PlacementOf(DockGroup group, string panelId)
    {
        var placement = new DockPlacement(group.Id, group.Panels.IndexOf(panelId));
        if (group.Panels.Count != 1 || group.Parent is not { } parent)
            return placement;

        var index = parent.Children.IndexOf(group);
        var neighborIndex = index > 0 ? index - 1 : index + 1;
        if (neighborIndex >= parent.Children.Count)
            return placement;

        var neighbor = parent.Children[neighborIndex];
        var after = neighborIndex < index;
        var edge = parent.Orientation == DockOrientation.Horizontal
            ? after ? DockEdge.Right : DockEdge.Left
            : after ? DockEdge.Bottom : DockEdge.Top;
        var total = parent.Children.Sum(c => c.Size);
        var share = group.Size / total;
        return placement with { NeighborId = neighbor.Id, Edge = edge, Fraction = share * (1 - share) / (neighbor.Size / total) };
    }

    private static Size Valid(Size size) =>
        new(double.IsFinite(size.Width) && size.Width >= 1 ? size.Width : 480, double.IsFinite(size.Height) && size.Height >= 1 ? size.Height : 360);

    private static bool IsAncestor(DockNode ancestor, DockNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static IEnumerable<DockNode> Descendants(DockNode node)
    {
        yield return node;
        if (node is not DockSplit split)
            yield break;
        foreach (var child in split.Children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    /// <summary>Runs changes that raise one <see cref="Changed"/> together, of the kind that covers them all.</summary>
    private void Batch(Action changes)
    {
        _batching++;
        try
        {
            changes();
        }
        finally
        {
            if (--_batching == 0 && _pending is { } kind)
            {
                _pending = null;
                Raise(kind);
            }
        }
    }

    private void Raise(DockChangeKind kind)
    {
        if (_batching > 0)
        {
            _pending = _pending is null || _pending == kind ? kind : DockChangeKind.Structure;
            return;
        }

        Changed?.Invoke(this, new DockLayoutChangedEventArgs(kind));
    }
}
