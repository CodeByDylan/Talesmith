using Avalonia;
using Avalonia.Controls;

namespace Talesmith.UI.Docking;

/// <summary>The window of a floating window of a dock layout: shows its panels, records where it is, and returns its panels to the workspace
/// when the user closes it.</summary>
internal sealed class DockWindow : Window
{
    public static readonly Size MinimumSize = new(240, 160);

    private readonly DockHost _main;
    private bool _quiet;
    private bool _returnPanels;

    public DockWindow(DockHost main, DockFloat floating)
    {
        _main = main;
        Float = floating;
        Host = new DockHost(main, floating);
        Content = Host;
        Classes.Add("dock-window");
        ShowInTaskbar = false;
        CanMinimize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        MinWidth = MinimumSize.Width;
        MinHeight = MinimumSize.Height;
        Position = floating.Position;
        Width = floating.Size.Width;
        Height = floating.Size.Height;
        UpdateTitle();

        main.Layout?.Changed += OnLayoutChanged;
        PositionChanged += (_, _) => Record();
        Resized += (_, _) => Record();
        Activated += (_, _) => main.OnWindowActivated(this);
    }

    public DockFloat Float { get; }

    public DockHost Host { get; }

    /// <summary>Closes the window without returning its panels, for when its floating window is gone or the application closes it.</summary>
    public void CloseQuietly()
    {
        _quiet = true;
        Close();
    }

    /// <summary>Moves the window to the middle of the main window's screen when its title bar is off every screen, such as on a screen that is no
    /// longer connected, and keeps it within that screen.</summary>
    public void KeepOnScreen(Window owner)
    {
        var screens = owner.Screens;
        if (screens.ScreenFromPoint(Position + new PixelPoint(48, 12)) is { } current)
        {
            Fit(current.WorkingArea, current.Scaling);
            return;
        }

        if (screens.ScreenFromWindow(owner) is not { } screen)
            return;
        var area = screen.WorkingArea;
        Fit(area, screen.Scaling);
        var size = PixelSize.FromSize(new Size(Width, Height), screen.Scaling);
        Position = new PixelPoint(area.X + Math.Max(0, (area.Width - size.Width) / 2), area.Y + Math.Max(0, (area.Height - size.Height) / 2));
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _returnPanels = !_quiet && e.CloseReason == WindowCloseReason.WindowClosing;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _main.Layout?.Changed -= OnLayoutChanged;
        Host.Layout = null;
        _main.OnWindowClosed(this, _returnPanels);
    }

    private void Fit(PixelRect area, double scaling)
    {
        Width = Math.Min(Width, area.Width / scaling);
        Height = Math.Min(Height, area.Height / scaling);
    }

    private void Record()
    {
        if (!_quiet && IsVisible)
            _main.Layout?.MoveFloat(Float.Id, Position, ClientSize);
    }

    private void OnLayoutChanged(object? sender, DockLayoutChangedEventArgs e)
    {
        if (e.Kind is DockChangeKind.Structure or DockChangeKind.Activation)
            UpdateTitle();
    }

    /// <summary>Titles the window after the panel shown in its focused group, or in its first group.</summary>
    private void UpdateTitle()
    {
        if (_main.Layout is not { } layout || !layout.Floats.Contains(Float))
            return;
        var group = layout.FocusedGroup is { } focused && ReferenceEquals(layout.FloatOf(focused), Float)
            ? focused
            : layout.Groups.FirstOrDefault(g => ReferenceEquals(layout.FloatOf(g), Float));
        if (group?.ActivePanel is { } id && _main.GetPanel(id) is { } panel)
            Title = panel.Title;
    }
}
