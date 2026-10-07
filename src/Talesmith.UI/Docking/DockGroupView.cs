using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Talesmith.UI.Docking;

/// <summary>Shows a <see cref="DockGroup"/>: a tab strip with the group's panels and the active panel's content.</summary>
[TemplatePart(TabsPart, typeof(DockTabStrip))]
[TemplatePart(ContentPart, typeof(Panel))]
[TemplatePart(HeaderPart, typeof(Control))]
[TemplatePart(OverflowPart, typeof(Button))]
[TemplatePart(MaximizePart, typeof(Button))]
[PseudoClasses(":focused-group", ":maximized", ":overflowing", ":empty")]
public class DockGroupView : TemplatedControl
{
    private const string TabsPart = "PART_Tabs";
    private const string ContentPart = "PART_Content";
    private const string HeaderPart = "PART_Header";
    private const string OverflowPart = "PART_Overflow";
    private const string MaximizePart = "PART_Maximize";

    public static readonly StyledProperty<Control?> HeaderActionsProperty =
        AvaloniaProperty.Register<DockGroupView, Control?>(nameof(HeaderActions));

    public static readonly StyledProperty<Geometry?> MaximizeIconProperty =
        AvaloniaProperty.Register<DockGroupView, Geometry?>(nameof(MaximizeIcon), Icons.Maximize2);

    private readonly DockHost _host;
    private readonly List<DockTab> _tabs = [];
    private DockTabStrip? _strip;
    private Panel? _content;
    private Control? _header;
    private Button? _overflow;
    private Button? _maximize;

    internal DockGroupView(DockHost host, DockGroup group)
    {
        _host = host;
        Group = group;
    }

    public DockGroup Group { get; }

    /// <summary>Gets the active panel's header actions.</summary>
    public Control? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        private set => SetValue(HeaderActionsProperty, value);
    }

    public Geometry? MaximizeIcon
    {
        get => GetValue(MaximizeIconProperty);
        private set => SetValue(MaximizeIconProperty, value);
    }

    internal IReadOnlyList<DockTab> Tabs => _tabs;

    internal Control? Header => _header;

    internal DockTabStrip? Strip => _strip;

    internal Panel? ContentArea => _content;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _strip?.Children.Clear();
        _strip?.PropertyChanged -= OnStripPropertyChanged;
        _content?.Children.Clear();
        foreach (var tab in _tabs)
            tab.CloseRequested -= OnTabCloseRequested;
        _tabs.Clear();
        _overflow?.Click -= OnOverflowClick;
        _maximize?.Click -= OnMaximizeClick;

        _strip = e.NameScope.Find<DockTabStrip>(TabsPart);
        _content = e.NameScope.Find<Panel>(ContentPart);
        _header = e.NameScope.Find<Control>(HeaderPart);
        _overflow = e.NameScope.Find<Button>(OverflowPart);
        _maximize = e.NameScope.Find<Button>(MaximizePart);

        _strip?.PropertyChanged += OnStripPropertyChanged;
        _overflow?.Click += OnOverflowClick;
        _maximize?.Click += OnMaximizeClick;
        Sync();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateState();
    }

    /// <summary>Releases the active panel's header actions, which a group of a replaced layout shows next.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        HeaderActions = null;
    }

    /// <summary>Shows the group's tabs, rebuilding them only when its panels changed, and places the panels' content.</summary>
    internal void Sync()
    {
        var panels = Group.Panels.Select(_host.GetPanel).OfType<IDockPanel>().ToList();
        if (!panels.SequenceEqual(_tabs.Select(t => t.Panel)))
        {
            foreach (var tab in _tabs)
                tab.CloseRequested -= OnTabCloseRequested;
            _tabs.Clear();
            _strip?.Children.Clear();
            foreach (var panel in panels)
            {
                var tab = new DockTab(panel);
                tab.CloseRequested += OnTabCloseRequested;
                tab.ContextFlyout = CreateTabMenu(tab);
                _tabs.Add(tab);
                _strip?.Children.Add(tab);
            }
        }

        if (_content is not null)
        {
            for (var i = _content.Children.Count - 1; i >= 0; i--)
            {
                if (!_tabs.Exists(t => ReferenceEquals(_host.GetContent(t.Panel), _content.Children[i])))
                    _content.Children.RemoveAt(i);
            }

            foreach (var tab in _tabs)
            {
                var content = _host.GetContent(tab.Panel);
                if (content.Parent is Panel parent && !ReferenceEquals(parent, _content))
                    parent.Children.Remove(content);
                if (content.Parent is null)
                    _content.Children.Add(content);
            }
        }

        PseudoClasses.Set(":empty", _tabs.Count == 0);
        UpdateState();
    }

    /// <summary>Updates the active tab, visible content and focus highlight without rebuilding; leaves alone the content of panels that moved to
    /// another group, which a group of another window may update after this one.</summary>
    internal void UpdateState()
    {
        var layout = _host.Layout;
        var focused = layout is not null && ReferenceEquals(layout.FocusedGroup, Group);
        var maximized = layout is not null && ReferenceEquals(layout.MaximizedGroup, Group);
        PseudoClasses.Set(":focused-group", focused);
        PseudoClasses.Set(":maximized", maximized);
        MaximizeIcon = maximized ? Icons.Minimize2 : Icons.Maximize2;
        if (_maximize is not null)
            ToolTip.SetTip(_maximize, maximized ? "Restore layout" : "Maximize");

        IDockPanel? active = null;
        foreach (var tab in _tabs)
        {
            var isActive = tab.Panel.Id == Group.ActivePanel;
            tab.IsActive = isActive;
            tab.IsGroupFocused = focused;
            var content = _host.GetContent(tab.Panel);
            if (ReferenceEquals(content.GetVisualParent(), _content))
                content.IsVisible = isActive;
            if (isActive)
                active = tab.Panel;
        }

        ShowHeaderActions(active?.HeaderActions);
        _strip?.InvalidateMeasure();
    }

    /// <summary>Shows a panel's header actions, taking them from the group that showed them until now, such as the one the panel left, whose
    /// views may update after this one.</summary>
    private void ShowHeaderActions(Control? actions)
    {
        if (actions?.GetVisualParent() is ContentPresenter { TemplatedParent: DockGroupView other } && !ReferenceEquals(other, this))
            other.HeaderActions = null;
        HeaderActions = actions;
    }

    private void OnStripPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DockTabStrip.IsOverflowingProperty)
            PseudoClasses.Set(":overflowing", _strip!.IsOverflowing);
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is DockTab tab)
            _host.ClosePanel(tab.Panel.Id);
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => _host.Layout?.ToggleMaximize(Group.Id);

    private void OnOverflowClick(object? sender, RoutedEventArgs e)
    {
        if (_overflow is null)
            return;
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var tab in _tabs)
        {
            var id = tab.Panel.Id;
            var item = new MenuItem
            {
                Header = tab.Panel.Title,
                Icon = tab.Panel.Icon is { } icon ? new Controls.SymbolIcon { Data = icon, Size = 14 } : null,
                FontWeight = tab.IsActive ? FontWeight.SemiBold : FontWeight.Normal
            };
            item.Click += (_, _) => _host.Layout?.ActivatePanel(id);
            menu.Items.Add(item);
        }

        menu.ShowAt(_overflow);
    }

    private MenuFlyout CreateTabMenu(DockTab tab)
    {
        var menu = new MenuFlyout();
        // A menu flyout whose items start out empty never shows the items added when it opens.
        Fill();
        menu.Opening += (_, _) => Fill();
        return menu;

        void Fill()
        {
            menu.Items.Clear();
            var id = tab.Panel.Id;
            var layout = _host.Layout;
            var maximized = layout is not null && ReferenceEquals(layout.MaximizedGroup, Group);

            var close = new MenuItem { Header = "Close", IsEnabled = tab.Panel.CanClose, Icon = new Controls.SymbolIcon { Data = Icons.X, Size = 14 } };
            close.Click += (_, _) => _host.ClosePanel(id);
            var closeOthers = new MenuItem { Header = "Close other tabs", IsEnabled = _tabs.Exists(t => t != tab && t.Panel.CanClose) };
            closeOthers.Click += (_, _) =>
            {
                foreach (var other in _tabs.Where(t => t != tab && t.Panel.CanClose).Select(t => t.Panel.Id).ToList())
                    _host.ClosePanel(other);
            };
            var maximize = new MenuItem
            {
                Header = maximized ? "Restore layout" : "Maximize",
                InputGesture = null,
                Icon = new Controls.SymbolIcon { Data = maximized ? Icons.Minimize2 : Icons.Maximize2, Size = 14 }
            };
            maximize.Click += (_, _) => _host.Layout?.ToggleMaximize(Group.Id);
            var aloneInWindow = layout?.FloatOf(Group) is { Root: DockGroup { Panels.Count: 1 } };
            var newWindow = new MenuItem
            {
                Header = "Move to new window",
                IsEnabled = layout?.CanFloat(id) == true && !aloneInWindow,
                Icon = new Controls.SymbolIcon { Data = Icons.ExternalLink, Size = 14 }
            };
            newWindow.Click += (_, _) => _host.FloatPanel(id, this);

            menu.Items.Add(close);
            menu.Items.Add(closeOthers);
            menu.Items.Add(new Separator());
            menu.Items.Add(maximize);
            menu.Items.Add(new Separator());
            menu.Items.Add(newWindow);
            if (_host.IsFloating)
            {
                var back = new MenuItem { Header = "Move to main window", Icon = new Controls.SymbolIcon { Data = Icons.Layout, Size = 14 } };
                back.Click += (_, _) => _host.Layout?.ReturnPanel(id);
                menu.Items.Add(back);
            }
        }
    }
}
