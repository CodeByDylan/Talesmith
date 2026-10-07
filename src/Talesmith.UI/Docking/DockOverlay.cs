using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Docking;

/// <summary>A button a dragged tab can be dropped on: one side of a group or of the whole workspace, or a group's tabs.</summary>
/// <param name="Edge">The side the panel docks to, or null for the group's tabs.</param>
internal sealed record DockGuide(DockEdge? Edge, bool IsWorkspace, Rect Bounds)
{
    /// <summary>The size of a guide; the pointer counts as over it a little beyond its edge.</summary>
    public const double Size = 32;

    public bool Contains(Point point) => Bounds.Inflate(3).Contains(point);
}

/// <summary>Draws a dragged tab's ghost, which also says where it would land, the guides it can be dropped on, the preview of the area it
/// would take, and a flash on the group a panel landed in, above the workspace; and outside the window, the window a tab dragged out of every
/// window would open in.</summary>
internal sealed class DockOverlay : Canvas
{
    private static readonly TimeSpan Motion = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan LandingTime = TimeSpan.FromMilliseconds(650);
    private static readonly Vector GhostOffset = new(14, 10);
    private const double GhostClearance = 6;

    private readonly Border _preview = new() { Classes = { "dock-drop-indicator" }, IsVisible = false, Child = new Border { Classes = { "dock-drop-fill" } } };
    private readonly Border _caret = new() { Classes = { "dock-drop-caret" }, IsVisible = false };
    private readonly Border _landing = new() { Classes = { "dock-landing" }, IsVisible = false };
    private readonly Border _ghost = new() { Classes = { "dock-drag-ghost" }, IsVisible = false };
    private readonly SymbolIcon _ghostIcon = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ghostTitle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SymbolIcon _ghostArrow = new() { Data = Icons.ChevronRight, Size = 12, Classes = { "dock-drag-arrow" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _ghostTarget = new() { Classes = { "dock-drag-target" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<(Border Button, DockGuideGlyph Glyph)> _guides = [];
    private readonly List<Rect> _guideAreas = [];
    private readonly Border _newWindow = new() { Classes = { "dock-new-window" }, IsHitTestVisible = false };
    private readonly SymbolIcon _newWindowIcon = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _newWindowTitle = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _newWindowPopup;

    public DockOverlay()
    {
        IsHitTestVisible = false;
        _ghost.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _ghostIcon, _ghostTitle, _ghostArrow, _ghostTarget } };
        var header = new Border
        {
            Classes = { "dock-new-window-header" },
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _newWindowIcon, _newWindowTitle } }
        };
        DockPanel.SetDock(header, Dock.Top);
        _newWindow.Child = new DockPanel
        {
            Children =
            {
                header,
                new TextBlock
                {
                    Text = "New window",
                    Classes = { "dock-new-window-hint" },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        // The popup's anchor is clipped to the overlay, so the preview is moved outside it by offsets from the overlay's corner.
        _newWindowPopup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementRect = new Rect(0, 0, 1, 1),
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX | PopupPositionerConstraintAdjustment.SlideY,
            IsLightDismissEnabled = false,
            Topmost = true,
            Child = _newWindow
        };
        Children.Add(_newWindowPopup);
        Children.Add(_landing);
        Children.Add(_preview);
        Children.Add(_caret);
        for (var i = 0; i < 9; i++)
        {
            var glyph = new DockGuideGlyph();
            var button = new Border { Classes = { "dock-guide" }, Width = DockGuide.Size, Height = DockGuide.Size, Child = glyph, IsVisible = false };
            _guides.Add((button, glyph));
            Children.Add(button);
        }

        Children.Add(_ghost);
    }

    public void ShowGhost(IDockPanel panel)
    {
        _ghostIcon.Data = panel.Icon;
        _ghostIcon.IsVisible = panel.Icon is not null;
        _ghostTitle.Text = panel.Title;
        _ghost.IsVisible = true;
    }

    /// <summary>Puts the ghost beside the pointer, keeping it inside the overlay and off the guides shown, so that it never hides one.</summary>
    public void MoveGhost(Point position)
    {
        _ghost.Measure(Size.Infinity);
        var size = _ghost.DesiredSize;
        var ghost = new Rect(position + GhostOffset, size);
        if (ghost.Right > Bounds.Width)
            ghost = ghost.WithX(Math.Max(0, position.X - GhostOffset.X - size.Width));
        if (ghost.Bottom > Bounds.Height)
            ghost = ghost.WithY(Math.Max(0, position.Y - GhostOffset.Y - size.Height));

        foreach (var area in _guideAreas)
        {
            if (!ghost.Intersects(area))
                continue;
            var below = area.Bottom + GhostClearance;
            ghost = ghost.WithY(below + size.Height <= Bounds.Height ? below : Math.Max(0, area.Top - GhostClearance - size.Height));
        }

        SetLeft(_ghost, ghost.X);
        SetTop(_ghost, ghost.Y);
    }

    /// <summary>Shows the guides a tab can be dropped on, the one under the pointer highlighted.</summary>
    public void ShowGuides(IReadOnlyList<DockGuide> guides, DockGuide? active)
    {
        _guideAreas.Clear();
        var compass = default(Rect?);
        foreach (var guide in guides)
        {
            if (guide.IsWorkspace)
                _guideAreas.Add(guide.Bounds);
            else
                compass = compass?.Union(guide.Bounds) ?? guide.Bounds;
        }

        if (compass is { } around)
            _guideAreas.Add(around);

        for (var i = 0; i < _guides.Count; i++)
        {
            var (button, glyph) = _guides[i];
            if (i >= guides.Count)
            {
                button.IsVisible = false;
                continue;
            }

            var guide = guides[i];
            SetLeft(button, guide.Bounds.X);
            SetTop(button, guide.Bounds.Y);
            glyph.Edge = guide.Edge;
            glyph.IsWorkspace = guide.IsWorkspace;
            var isActive = ReferenceEquals(guide, active) || guide == active;
            button.Classes.Set("active", isActive);
            glyph.IsActive = isActive;
            button.IsVisible = true;
        }
    }

    /// <summary>Shows the area a dropped tab would take, and on the ghost where that is, or nothing when it would land nowhere.</summary>
    /// <param name="caret">Where a tab dropped among others would go, for drops on a tab strip.</param>
    public void ShowTarget(Rect? preview, string? label, Rect? caret)
    {
        Show(_caret, caret);
        Show(_preview, preview);
        _ghostTarget.Text = label;
        _ghostArrow.IsVisible = _ghostTarget.IsVisible = preview is not null && label is not null;
    }

    /// <summary>Shows the window a dragged panel would open in at <paramref name="bounds"/>, in this overlay's coordinates, or hides it.</summary>
    public void ShowNewWindow(IDockPanel? panel, Rect? bounds)
    {
        if (panel is null || bounds is not { } area)
        {
            _newWindowPopup.IsOpen = false;
            return;
        }

        _newWindowIcon.Data = panel.Icon;
        _newWindowIcon.IsVisible = panel.Icon is not null;
        _newWindowTitle.Text = panel.Title;
        _newWindow.Width = area.Width;
        _newWindow.Height = area.Height;
        _newWindowPopup.HorizontalOffset = area.X;
        _newWindowPopup.VerticalOffset = area.Y;
        _newWindowPopup.IsOpen = true;
    }

    /// <summary>Flashes the outline of the group a panel was dropped in, so it is clear where it went.</summary>
    public void ShowLanding(Rect bounds)
    {
        _landing.Transitions = null;
        Place(_landing, bounds);
        _landing.Opacity = 1;
        _landing.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            _landing.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = LandingTime, Easing = new CubicEaseIn() }];
            _landing.Opacity = 0;
        }, DispatcherPriority.Render);
    }

    /// <summary>Hides everything but a landing flash in progress.</summary>
    public void Hide()
    {
        foreach (var child in new Control[] { _preview, _caret, _ghost })
        {
            child.Transitions = null;
            child.IsVisible = false;
        }

        foreach (var (button, _) in _guides)
            button.IsVisible = false;
        _guideAreas.Clear();
    }

    /// <summary>Moves a control to <paramref name="rect"/>, gliding there when it was shown already, or hides it when the rect is null.</summary>
    private static void Show(Control control, Rect? rect)
    {
        if (rect is not { } r)
        {
            control.IsVisible = false;
            control.Transitions = null;
            return;
        }

        if (!control.IsVisible)
        {
            control.Transitions = null;
            Place(control, r);
            control.IsVisible = true;
            control.Transitions =
            [
                new DoubleTransition { Property = LeftProperty, Duration = Motion, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = TopProperty, Duration = Motion, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = WidthProperty, Duration = Motion, Easing = new CubicEaseOut() },
                new DoubleTransition { Property = HeightProperty, Duration = Motion, Easing = new CubicEaseOut() },
            ];
            return;
        }

        Place(control, r);
    }

    private static void Place(Control control, Rect rect)
    {
        SetLeft(control, rect.X);
        SetTop(control, rect.Y);
        control.Width = rect.Width;
        control.Height = rect.Height;
    }
}

/// <summary>The picture on a drop guide: a frame with the part a dropped panel takes filled, a half of a group, a band along the workspace, or
/// the whole frame for a group's tabs.</summary>
internal sealed class DockGuideGlyph : Control
{
    private const double Frame = 18;

    private DockEdge? _edge;
    private bool _isWorkspace;
    private bool _isActive;

    public DockEdge? Edge
    {
        get => _edge;
        set
        {
            if (_edge == value)
                return;
            _edge = value;
            InvalidateVisual();
        }
    }

    public bool IsWorkspace
    {
        get => _isWorkspace;
        set
        {
            if (_isWorkspace == value)
                return;
            _isWorkspace = value;
            InvalidateVisual();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;
            _isActive = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var frame = new Rect((Bounds.Width - Frame) / 2, (Bounds.Height - Frame) / 2, Frame, Frame);
        var stroke = Brush(IsActive ? "AccentForegroundBrush" : "TextSecondaryBrush");
        var fill = Brush(IsActive ? "AccentForegroundBrush" : "AccentBrush");
        var share = IsWorkspace ? 0.34 : 0.5;
        var part = Edge switch
        {
            DockEdge.Left => new Rect(frame.X, frame.Y, frame.Width * share, frame.Height),
            DockEdge.Right => new Rect(frame.Right - frame.Width * share, frame.Y, frame.Width * share, frame.Height),
            DockEdge.Top => new Rect(frame.X, frame.Y, frame.Width, frame.Height * share),
            DockEdge.Bottom => new Rect(frame.X, frame.Bottom - frame.Height * share, frame.Width, frame.Height * share),
            _ => frame.Deflate(new Thickness(0, 5, 0, 0))
        };
        context.DrawRectangle(fill, null, part, 2, 2);
        if (Edge is null)
            context.DrawRectangle(fill, null, new Rect(frame.X, frame.Y, frame.Width * 0.45, 4), 1, 1);
        context.DrawRectangle(null, new Pen(stroke, 1.5), frame.Deflate(0.75), 3, 3);
    }

    private IBrush? Brush(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : null;
}
