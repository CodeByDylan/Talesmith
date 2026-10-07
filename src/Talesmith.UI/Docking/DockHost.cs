using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Docking;

/// <summary>The arguments of <see cref="DockHost.WindowOpenedEvent"/>.</summary>
public sealed class DockWindowOpenedEventArgs(Window window) : RoutedEventArgs(DockHost.WindowOpenedEvent)
{
    /// <summary>Gets the floating window, which shows next.</summary>
    public Window Window { get; } = window;
}

/// <summary>A dock workspace: renders a <see cref="DockLayout"/> as resizable splits of tab groups whose tabs can be dragged to rearrange
/// panels, and opens a window for each of the layout's floating windows.</summary>
/// <remarks>
/// Panels come from <see cref="ContentProvider"/>. Each panel's content control is created once and kept while the panel is moved, also
/// between windows, hidden behind another tab, maximized or closed, so its state survives every layout change. A tab dragged outside every
/// window opens in a window of its own, and closing that window returns its panels to the workspace.
/// </remarks>
public class DockHost : Control
{
    public static readonly StyledProperty<DockLayout?> LayoutProperty =
        AvaloniaProperty.Register<DockHost, DockLayout?>(nameof(Layout));

    public static readonly StyledProperty<IDockContentProvider?> ContentProviderProperty =
        AvaloniaProperty.Register<DockHost, IDockContentProvider?>(nameof(ContentProvider));

    /// <summary>Raised before a floating window shows, so the application can set it up like its own windows, such as for its shortcuts.</summary>
    public static readonly RoutedEvent<DockWindowOpenedEventArgs> WindowOpenedEvent =
        RoutedEvent.Register<DockHost, DockWindowOpenedEventArgs>(nameof(WindowOpened), RoutingStrategies.Bubble);

    private const double DragThreshold = 6;
    private const double WorkspaceEdgeBand = 22;
    private const double EdgeZone = 0.3;
    private const double CompassStep = 38;
    private const double CompassRoom = 3 * CompassStep + 8;
    private const double GuideInset = 10;

    private static readonly DockEdge[] Edges = [DockEdge.Left, DockEdge.Top, DockEdge.Right, DockEdge.Bottom];
    private static readonly Size MaximumNewWindowSize = new(960, 720);

    /// <summary>Where the pointer holds a window a dragged tab would open in, from the window's top-left corner.</summary>
    private static readonly Vector NewWindowGrip = new(48, 16);

    private readonly DockHost? _main;
    private readonly DockFloat? _float;
    private readonly Grid _root = new();
    private readonly DockWorkspace _workspace = new();
    private readonly DockOverlay _overlay = new();
    private readonly Dictionary<DockGroup, DockGroupView> _groups = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, IDockPanel> _panels = new(StringComparer.Ordinal);
    private readonly Dictionary<IDockPanel, Control> _contents = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DockFloat, DockWindow> _windows = new(ReferenceEqualityComparer.Instance);
    private readonly List<DockWindow> _stack = [];
    private readonly EmptyState _emptyState = new() { Icon = Icons.Layout, Title = "No panels open", Hint = "Open a panel from the Window menu." };
    private Window? _owner;
    private DockGroup? _followedFocus;
    private TabDrag? _drag;

    public DockHost()
    {
        _root.Children.Add(_workspace);
        _root.Children.Add(_overlay);
        LogicalChildren.Add(_root);
        VisualChildren.Add(_root);

        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, OnTabPointerPressed, RoutingStrategies.Bubble);
        AddHandler(PointerMovedEvent, OnDragPointerMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnDragPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnDragCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Creates the host of a floating window, which shares the panels of the main host.</summary>
    internal DockHost(DockHost main, DockFloat floating)
        : this()
    {
        _main = main;
        _float = floating;
        ContentProvider = main.ContentProvider;
        Layout = main.Layout;
    }

    /// <summary>Gets or sets the layout shown; changes to it are reflected immediately.</summary>
    public DockLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    /// <summary>Gets or sets the source of the panels named by the layout.</summary>
    public IDockContentProvider? ContentProvider
    {
        get => GetValue(ContentProviderProperty);
        set => SetValue(ContentProviderProperty, value);
    }

    /// <summary>Gets whether a tab is being dragged.</summary>
    public bool IsDragging => _drag is { IsActive: true };

    /// <summary>Raised before a floating window shows.</summary>
    public event EventHandler<DockWindowOpenedEventArgs>? WindowOpened
    {
        add => AddHandler(WindowOpenedEvent, value);
        remove => RemoveHandler(WindowOpenedEvent, value);
    }

    /// <summary>Gets whether this host shows a floating window rather than the workspace.</summary>
    internal bool IsFloating => _float is not null;

    /// <summary>Gets the floating windows open, from the bottom one to the top one.</summary>
    internal IReadOnlyList<Window> Windows => Main._stack;

    /// <summary>The host of the main window, which keeps the panels' content and the floating windows.</summary>
    private DockHost Main => _main ?? this;

    /// <summary>The root of the tree this host shows: the workspace's, or its floating window's while the window is in the layout.</summary>
    private DockNode? TreeRoot => _float is null ? Layout?.Root : Layout?.Floats.Contains(_float) == true ? _float.Root : null;

    /// <summary>The hosts of every window, the floating windows from the top one down, then the main window's.</summary>
    private IEnumerable<DockHost> Hosts => [.. Enumerable.Reverse(Main._stack).Select(w => w.Host), Main];

    /// <summary>Gets the content control of a panel, creating it on first use; null when the provider does not know the panel.</summary>
    public Control? GetContent(string panelId) => GetPanel(panelId) is { } panel ? GetContent(panel) : null;

    /// <summary>Closes a panel when it allows closing; its content is kept for when it is shown again.</summary>
    public bool ClosePanel(string panelId)
    {
        if (Layout is null || GetPanel(panelId) is { CanClose: false })
            return false;
        return Layout.ClosePanel(panelId);
    }

    internal IDockPanel? GetPanel(string panelId)
    {
        if (_main is not null)
            return _main.GetPanel(panelId);
        if (_panels.TryGetValue(panelId, out var panel))
            return panel;
        panel = ContentProvider?.GetPanel(panelId);
        if (panel is not null)
            _panels[panelId] = panel;
        return panel;
    }

    internal Control GetContent(IDockPanel panel)
    {
        if (_main is not null)
            return _main.GetContent(panel);
        if (!_contents.TryGetValue(panel, out var content))
        {
            content = panel.Content;
            content.Classes.Add("dock-content");
            _contents[panel] = content;
        }

        return content;
    }

    /// <summary>Moves a panel into a new window over the group it is in, as large as the group.</summary>
    internal void FloatPanel(string panelId, DockGroupView view)
    {
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        Layout?.FloatPanel(panelId, view.PointToScreen(default) + PixelPoint.FromPoint(new Point(32, 32), scaling), NewWindowSize(view.Bounds.Size));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _root.Measure(availableSize);
        return _root.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _root.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LayoutProperty)
        {
            if (change.OldValue is DockLayout old)
                old.Changed -= OnLayoutChanged;
            if (change.NewValue is DockLayout layout)
                layout.Changed += OnLayoutChanged;
            CancelDrag();
            Reconcile();
            if (_float is null)
            {
                _followedFocus = Layout?.FocusedGroup;
                SyncWindows();
            }
        }
        else if (change.Property == ContentProviderProperty)
        {
            _panels.Clear();
            Reconcile();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_float is not null || TopLevel.GetTopLevel(this) is not Window owner)
            return;
        _owner = owner;
        owner.Opened += OnOwnerOpened;
        SyncWindows();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_float is not null)
            return;
        if (_owner is not null)
            _owner.Opened -= OnOwnerOpened;
        _owner = null;
        foreach (var window in _windows.Values.ToList())
            window.CloseQuietly();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _drag is not null)
        {
            CancelDrag();
            e.Handled = true;
        }
    }

    private void OnLayoutChanged(object? sender, DockLayoutChangedEventArgs e)
    {
        switch (e.Kind)
        {
            case DockChangeKind.Structure:
                Reconcile();
                if (_float is null)
                {
                    SyncWindows();
                    FollowFocus();
                }

                break;
            case DockChangeKind.Activation:
                foreach (var view in _groups.Values)
                    view.UpdateState();
                if (_float is null)
                    FollowFocus();
                break;
            case DockChangeKind.Sizes:
                _workspace.InvalidateMeasure();
                break;
            case DockChangeKind.Bounds:
                break;
        }
    }

    /// <summary>Shows the groups of this host's tree: keeps the views of groups that stay, adds views for new groups and drops those of groups
    /// that are gone, then lets the workspace place them.</summary>
    private void Reconcile()
    {
        if (Layout is not { } layout || TreeRoot is not { } root)
        {
            _groups.Clear();
            _workspace.Children.Clear();
            return;
        }

        var live = layout.Groups.Where(g => ReferenceEquals(layout.FloatOf(g), _float)).ToHashSet<DockGroup>(ReferenceEqualityComparer.Instance);
        foreach (var stale in _groups.Keys.Where(g => !live.Contains(g)).ToList())
            _groups.Remove(stale);
        foreach (var group in live)
        {
            if (!_groups.ContainsKey(group))
                _groups[group] = new DockGroupView(this, group);
        }

        foreach (var view in _groups.Values)
            view.Sync();
        var maximized = layout.MaximizedGroup is { } candidate && live.Contains(candidate) ? candidate : null;
        _workspace.Show(layout, root, maximized, group => _groups[group], _emptyState);
    }

    private void OnOwnerOpened(object? sender, EventArgs e) => SyncWindows();

    /// <summary>Opens a window for each floating window of the layout without one, and closes the windows of floating windows that are gone.</summary>
    private void SyncWindows()
    {
        var floats = Layout?.Floats ?? [];
        foreach (var (floating, window) in _windows.ToList())
        {
            if (!floats.Contains(floating))
                window.CloseQuietly();
        }

        if (_owner is not { IsVisible: true } owner)
            return;
        foreach (var floating in floats)
        {
            if (_windows.ContainsKey(floating))
                continue;
            var window = new DockWindow(this, floating);
            _windows[floating] = window;
            _stack.Add(window);
            window.KeepOnScreen(owner);
            RaiseEvent(new DockWindowOpenedEventArgs(window));
            window.Show(owner);
        }
    }

    /// <summary>Forgets a floating window that closed, and returns its panels to the workspace when the user closed it.</summary>
    internal void OnWindowClosed(DockWindow window, bool returnPanels)
    {
        if (_windows.TryGetValue(window.Float, out var known) && ReferenceEquals(known, window))
            _windows.Remove(window.Float);
        _stack.Remove(window);
        if (returnPanels)
            Layout?.CloseFloat(window.Float.Id);
    }

    /// <summary>Keeps the floating windows in the order they were last activated, which is the order they overlap in.</summary>
    internal void OnWindowActivated(DockWindow window)
    {
        if (_stack.Remove(window))
            _stack.Add(window);
    }

    /// <summary>Brings the window of the focused group forward when focus moved to another window's group, such as when a panel is shown from a
    /// menu or a floating window closed.</summary>
    private void FollowFocus()
    {
        var focused = Layout?.FocusedGroup;
        if (ReferenceEquals(focused, _followedFocus))
            return;
        _followedFocus = focused;
        if (focused is null)
            return;

        Window? window = Layout!.FloatOf(focused) is { } floating ? _windows.GetValueOrDefault(floating) : _owner;
        if (window is { IsVisible: true, IsActive: false })
            window.Activate();
    }

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        if (Layout is { } layout && FindAncestor<DockGroupView>(e.Source) is { } view)
            layout.FocusGroup(view.Group.Id);
    }

    private void OnDescendantGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (Layout is { } layout && FindAncestor<DockGroupView>(e.Source) is { } view)
            layout.FocusGroup(view.Group.Id);
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Layout is not { } layout || FindAncestor<DockTab>(e.Source) is not { } tab || FindAncestor<DockGroupView>(tab) is not { } view)
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsMiddleButtonPressed)
        {
            ClosePanel(tab.Panel.Id);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            layout.ToggleMaximize(view.Group.Id);
            e.Handled = true;
            return;
        }

        layout.ActivatePanel(tab.Panel.Id);
        _drag = new TabDrag(tab.Panel, view, point.Position);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnDragPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_drag is not { } drag)
            return;

        var position = e.GetPosition(this);
        if (!drag.IsActive)
        {
            if (Point.Distance(position, drag.Start) < DragThreshold)
                return;
            drag.IsActive = true;
            SetTabDragging(drag, true);
        }

        Main.UpdateDrag(drag, this.PointToScreen(position));
        e.Handled = true;
    }

    private void OnDragPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag is not { } drag)
            return;

        _drag = null;
        e.Pointer.Capture(null);
        Main.HideDrag();
        SetTabDragging(drag, false);
        if (drag.IsActive)
            Main.Drop(drag);
        e.Handled = true;
    }

    private void OnDragCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelDrag();

    private void CancelDrag()
    {
        if (_drag is not { } drag)
            return;
        _drag = null;
        Main.HideDrag();
        SetTabDragging(drag, false);
    }

    /// <summary>Shows where a dragged tab would land with the pointer at <paramref name="screen"/>: in the window under it, or in a new window
    /// when it is outside every window.</summary>
    private void UpdateDrag(TabDrag drag, PixelPoint screen)
    {
        var target = HostAt(screen, out var overWindow);
        foreach (var host in Hosts)
        {
            if (!ReferenceEquals(host, target))
                host._overlay.Hide();
        }

        drag.Host = target;
        drag.Target = target?.ShowDrag(drag, target.PointToClient(screen));
        drag.NewWindow = target is null && !overWindow && Layout?.CanFloat(drag.Panel.Id) == true ? NewWindowFor(drag, screen) : null;
        _overlay.ShowNewWindow(drag.Panel, drag.NewWindow is { } bounds ? new Rect(this.PointToClient(bounds.Position), bounds.Size) : null);
    }

    /// <summary>Shows where a dragged tab would land at <paramref name="position"/> in this host, and returns that place.</summary>
    private DropTarget? ShowDrag(TabDrag drag, Point position)
    {
        var guides = new List<DockGuide>();
        var target = FindTarget(drag, position, guides, out var active);
        _overlay.ShowGhost(drag.Panel);
        _overlay.ShowGuides(guides, active);
        _overlay.ShowTarget(target?.Preview, target?.Label, target?.Caret);
        _overlay.MoveGhost(position);
        return target;
    }

    private void HideDrag()
    {
        foreach (var host in Hosts)
            host._overlay.Hide();
        _overlay.ShowNewWindow(null, null);
    }

    /// <summary>The host under a point on screen; <paramref name="overWindow"/> tells whether the point is over one of the windows at all, such as
    /// over the main window's menu.</summary>
    private DockHost? HostAt(PixelPoint screen, out bool overWindow)
    {
        overWindow = false;
        foreach (var host in Hosts)
        {
            if (TopLevel.GetTopLevel(host) is not { IsVisible: true } top || !new Rect(top.ClientSize).Contains(top.PointToClient(screen)))
                continue;
            overWindow = true;
            return host.IsEffectivelyVisible && new Rect(host.Bounds.Size).Contains(host.PointToClient(screen)) ? host : null;
        }

        return null;
    }

    /// <summary>Where a new window for a dragged panel would go: as large as the panel's group, held by the pointer near its top-left corner.</summary>
    private NewWindow NewWindowFor(TabDrag drag, PixelPoint screen)
    {
        var scaling = _owner?.Screens.ScreenFromPoint(screen)?.Scaling ?? TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        return new NewWindow(screen - PixelPoint.FromPoint((Point)NewWindowGrip, scaling), NewWindowSize(drag.Source.Bounds.Size));
    }

    private static Size NewWindowSize(Size group) => new(
        Math.Clamp(group.Width, DockWindow.MinimumSize.Width, MaximumNewWindowSize.Width),
        Math.Clamp(group.Height, DockWindow.MinimumSize.Height, MaximumNewWindowSize.Height));

    private static void SetTabDragging(TabDrag drag, bool dragging)
    {
        foreach (var tab in drag.Source.Tabs)
        {
            if (ReferenceEquals(tab.Panel, drag.Panel))
                tab.IsDragging = dragging;
        }
    }

    private void Drop(TabDrag drag)
    {
        if (Layout is not { } layout)
            return;

        if (drag.NewWindow is { } bounds)
            layout.FloatPanel(drag.Panel.Id, bounds.Position, bounds.Size);
        else if (drag.Host is { } host && drag.Target is { } target)
            host.Drop(drag, target);
        else
            return;

        if (GetContent(drag.Panel) is { } content)
            content.Focus();
        var panel = drag.Panel;
        Dispatcher.UIThread.Post(() => ShowLanding(panel), DispatcherPriority.Loaded);
    }

    private void Drop(TabDrag drag, DropTarget target)
    {
        var layout = Layout!;
        var id = drag.Panel.Id;
        switch (target.Kind)
        {
            case DropKind.Tab:
                layout.MovePanel(id, target.Group!.Group.Id, target.TabIndex);
                break;
            case DropKind.Center:
                if (!ReferenceEquals(target.Group!.Group, drag.Source.Group))
                    layout.MovePanel(id, target.Group.Group.Id);
                break;
            case DropKind.Edge:
                layout.DockPanel(id, target.Group!.Group.Id, target.Edge);
                break;
            case DropKind.Workspace:
                if (TreeRoot is { } root)
                    layout.DockPanel(id, root.Id, target.Edge, 0.25);
                break;
        }
    }

    /// <summary>Flashes the outline of the group a panel is in, once its window placed it.</summary>
    private void ShowLanding(IDockPanel panel)
    {
        if (Layout?.FindPanel(panel.Id) is not { } group)
            return;
        foreach (var host in Hosts)
        {
            if (host._groups.TryGetValue(group, out var view) && view.IsEffectivelyVisible && view.TranslatePoint(default, host) is { } origin)
                host._overlay.ShowLanding(new Rect(origin, view.Bounds.Size).Deflate(1));
        }
    }

    /// <summary>Finds where a dragged tab would land: on the guide under the pointer, else among the tabs under it, along the workspace's edge, or
    /// on a side or the middle of the group under it; adds the guides to show for the pointer's position to <paramref name="guides"/>.</summary>
    private DropTarget? FindTarget(TabDrag drag, Point position, List<DockGuide> guides, out DockGuide? active)
    {
        active = null;
        if (Layout is not { } layout || TreeRoot is not { } root)
            return null;
        var workspace = new Rect(_workspace.Bounds.Size);
        var maximized = layout.MaximizedGroup is { } group && _groups.ContainsKey(group);
        var sourceIsOnlyGroup = ReferenceEquals(root, drag.Source.Group) && drag.Source.Group.Panels.Count == 1;
        var dockToWorkspace = !maximized && !sourceIsOnlyGroup && root is not DockGroup { Panels.Count: <= 1 };
        if (dockToWorkspace)
        {
            foreach (var edge in Edges)
                guides.Add(new DockGuide(edge, true, ClearOfTabs(WorkspaceGuide(workspace, edge))));
        }

        var view = GroupAt(position);
        var body = view is null ? default : BodyOf(view);
        var isSource = ReferenceEquals(view, drag.Source);
        var isAlone = isSource && drag.Source.Group.Panels.Count == 1;
        if (view is not null && !isAlone && body.Width >= CompassRoom && body.Height >= CompassRoom)
            AddCompass(guides, body.Center, includeTabs: !isSource);

        active = guides.Find(g => g.Contains(position));
        if (active is { IsWorkspace: true, Edge: { } workspaceEdge })
            return WorkspaceTarget(workspace, workspaceEdge);
        if (active is { } guide && view is not null)
            return guide.Edge is { } guideEdge ? EdgeTarget(view, body, guideEdge, drag) : CenterTarget(view, body, drag);

        if (view?.Header is { } header && header.TranslatePoint(default, this) is { } headerOrigin
            && new Rect(headerOrigin, header.Bounds.Size).Contains(position))
        {
            return TabTarget(view, position, headerOrigin, header.Bounds.Size, body, drag);
        }

        if (dockToWorkspace && workspace.Contains(position))
        {
            var edge = NearestEdge(position, workspace, out var distance);
            if (distance < WorkspaceEdgeBand)
                return WorkspaceTarget(workspace, edge);
        }

        if (view is null || isAlone)
            return null;
        var rx = (position.X - body.X) / Math.Max(1, body.Width);
        var ry = (position.Y - body.Y) / Math.Max(1, body.Height);
        var nearest = NearestEdge(new Point(rx, ry), new Rect(0, 0, 1, 1), out var relative);
        if (relative < EdgeZone)
            return EdgeTarget(view, body, nearest, drag);
        return isSource ? null : CenterTarget(view, body, drag);
    }

    /// <summary>Adds the guides around a group's middle: one per side, and one for its tabs, leaving out those that would cover a guide of the
    /// workspace.</summary>
    private static void AddCompass(List<DockGuide> guides, Point center, bool includeTabs)
    {
        foreach (var edge in (DockEdge?[])[null, DockEdge.Left, DockEdge.Top, DockEdge.Right, DockEdge.Bottom])
        {
            if (edge is null && !includeTabs)
                continue;
            var offset = edge switch
            {
                DockEdge.Left => new Vector(-CompassStep, 0),
                DockEdge.Right => new Vector(CompassStep, 0),
                DockEdge.Top => new Vector(0, -CompassStep),
                DockEdge.Bottom => new Vector(0, CompassStep),
                _ => default
            };
            var bounds = new Rect(center + offset - new Vector(DockGuide.Size / 2, DockGuide.Size / 2), new Size(DockGuide.Size, DockGuide.Size));
            if (!guides.Exists(g => g.IsWorkspace && g.Bounds.Inflate(4).Intersects(bounds)))
                guides.Add(new DockGuide(edge, false, bounds));
        }
    }

    /// <summary>Moves a guide below the tabs it would cover, so that tabs can always be dropped among.</summary>
    private Rect ClearOfTabs(Rect guide)
    {
        foreach (var view in _groups.Values)
        {
            if (view.IsEffectivelyVisible && view.Header is { } header && header.TranslatePoint(default, this) is { } origin
                && new Rect(origin, header.Bounds.Size).Intersects(guide))
            {
                guide = guide.WithY(origin.Y + header.Bounds.Height + GuideInset);
            }
        }

        return guide;
    }

    /// <summary>The visible group under a point.</summary>
    private DockGroupView? GroupAt(Point position)
    {
        foreach (var view in _groups.Values)
        {
            if (view.IsEffectivelyVisible && view.TranslatePoint(default, this) is { } origin && new Rect(origin, view.Bounds.Size).Contains(position))
                return view;
        }

        return null;
    }

    /// <summary>A group's content area below its tabs, in this control's coordinates.</summary>
    private Rect BodyOf(DockGroupView view)
    {
        if (view.ContentArea is { } area && area.TranslatePoint(default, this) is { } origin)
            return new Rect(origin, area.Bounds.Size);
        return view.TranslatePoint(default, this) is { } viewOrigin ? new Rect(viewOrigin, view.Bounds.Size) : default;
    }

    private static Rect WorkspaceGuide(Rect workspace, DockEdge edge)
    {
        var size = new Size(DockGuide.Size, DockGuide.Size);
        var corner = workspace.Center - new Vector(DockGuide.Size / 2, DockGuide.Size / 2);
        return edge switch
        {
            DockEdge.Left => new Rect(new Point(workspace.X + GuideInset, corner.Y), size),
            DockEdge.Right => new Rect(new Point(workspace.Right - GuideInset - DockGuide.Size, corner.Y), size),
            DockEdge.Top => new Rect(new Point(corner.X, workspace.Y + GuideInset), size),
            _ => new Rect(new Point(corner.X, workspace.Bottom - GuideInset - DockGuide.Size), size)
        };
    }

    private static DropTarget WorkspaceTarget(Rect workspace, DockEdge edge)
    {
        var band = Math.Clamp((edge is DockEdge.Left or DockEdge.Right ? workspace.Width : workspace.Height) * 0.25, 120, 360);
        var label = edge switch
        {
            DockEdge.Left => "Left edge of the window",
            DockEdge.Right => "Right edge of the window",
            DockEdge.Top => "Top of the window",
            _ => "Bottom of the window"
        };
        return new DropTarget(DropKind.Workspace, null, edge, -1, EdgeRect(workspace, edge, band).Deflate(4), null, label);
    }

    private DropTarget EdgeTarget(DockGroupView view, Rect body, DockEdge edge, TabDrag drag)
    {
        var size = edge is DockEdge.Left or DockEdge.Right ? body.Width / 2 : body.Height / 2;
        var place = edge switch
        {
            DockEdge.Left => "Left of",
            DockEdge.Right => "Right of",
            DockEdge.Top => "Above",
            _ => "Below"
        };
        return new DropTarget(DropKind.Edge, view, edge, -1, EdgeRect(body, edge, size).Deflate(4), null, $"{place} {TitleOf(view, drag)}");
    }

    private DropTarget CenterTarget(DockGroupView view, Rect body, TabDrag drag) =>
        new(DropKind.Center, view, default, -1, body.Deflate(4), null, $"Tab next to {TitleOf(view, drag)}");

    /// <summary>The title a group goes by while a tab is dragged: its active panel's, or another one's when the dragged panel is active.</summary>
    private string TitleOf(DockGroupView view, TabDrag drag)
    {
        var group = view.Group;
        var id = group.ActivePanel == drag.Panel.Id ? group.Panels.FirstOrDefault(p => p != drag.Panel.Id) : group.ActivePanel;
        return id is not null && GetPanel(id) is { } panel ? panel.Title : "this group";
    }

    private DropTarget TabTarget(DockGroupView view, Point position, Point headerOrigin, Size headerSize, Rect body, TabDrag drag)
    {
        var index = 0;
        var caret = headerOrigin.X + 4;
        foreach (var tab in view.Tabs)
        {
            if (tab.IsOverflowed || tab.TranslatePoint(default, this) is not { } tabOrigin)
            {
                index++;
                continue;
            }

            var mid = tabOrigin.X + tab.Bounds.Width / 2;
            if (position.X < mid)
            {
                caret = tabOrigin.X;
                break;
            }

            caret = tabOrigin.X + tab.Bounds.Width;
            index++;
        }

        var rect = new Rect(caret - 1, headerOrigin.Y + 6, 2, Math.Max(0, headerSize.Height - 12));
        if (ReferenceEquals(view, drag.Source))
            return new DropTarget(DropKind.Tab, view, default, index, null, rect, null);
        return new DropTarget(DropKind.Tab, view, default, index, body.Deflate(4), rect, $"Tab next to {TitleOf(view, drag)}");
    }

    private static DockEdge NearestEdge(Point point, Rect area, out double distance)
    {
        var left = point.X - area.Left;
        var right = area.Right - point.X;
        var top = point.Y - area.Top;
        var bottom = area.Bottom - point.Y;
        distance = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (distance == left)
            return DockEdge.Left;
        if (distance == right)
            return DockEdge.Right;
        return distance == top ? DockEdge.Top : DockEdge.Bottom;
    }

    private static Rect EdgeRect(Rect area, DockEdge edge, double size) => edge switch
    {
        DockEdge.Left => new Rect(area.X, area.Y, size, area.Height),
        DockEdge.Right => new Rect(area.Right - size, area.Y, size, area.Height),
        DockEdge.Top => new Rect(area.X, area.Y, area.Width, size),
        _ => new Rect(area.X, area.Bottom - size, area.Width, size)
    };

    private static T? FindAncestor<T>(object? source) where T : class
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is T match)
                return match;
            if (visual is DockHost)
                return null;
        }

        return null;
    }

    private enum DropKind
    {
        Tab,
        Center,
        Edge,
        Workspace
    }

    /// <param name="Preview">The area the panel would take, or null when it stays where it is, as for a tab moved along its own group's tabs.</param>
    /// <param name="Caret">Where the tab would go among others, for drops on a tab strip.</param>
    /// <param name="Label">Where the panel would go, such as "Left of Scene".</param>
    private sealed record DropTarget(DropKind Kind, DockGroupView? Group, DockEdge Edge, int TabIndex, Rect? Preview, Rect? Caret, string? Label);

    /// <param name="Position">The window's top-left corner on screen.</param>
    /// <param name="Size">The size of the window's content.</param>
    private readonly record struct NewWindow(PixelPoint Position, Size Size);

    private sealed class TabDrag(IDockPanel panel, DockGroupView source, Point start)
    {
        public IDockPanel Panel { get; } = panel;

        public DockGroupView Source { get; } = source;

        public Point Start { get; } = start;

        public bool IsActive { get; set; }

        /// <summary>The host the pointer is over, whose place <see cref="Target"/> is.</summary>
        public DockHost? Host { get; set; }

        public DropTarget? Target { get; set; }

        /// <summary>Where the window the panel would open in goes, while the pointer is outside every window.</summary>
        public NewWindow? NewWindow { get; set; }
    }
}
