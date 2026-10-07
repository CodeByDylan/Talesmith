using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Talesmith.UI.Docking;

namespace Talesmith.Editor.Shell;

/// <summary>The editor layout: app bar, tool options, dock workspace and status bar.</summary>
public partial class ShellView : UserControl
{
    private ShellViewModel? _shell;

    public ShellView()
    {
        InitializeComponent();
        AppBar.Title = DocumentTitleText;
    }

    private DockHost Host => Workspace;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is not ShellViewModel shell || ReferenceEquals(shell, _shell))
            return;
        _shell = shell;
        Host.ContentProvider = shell.Panels;
        shell.Layout.LayoutReplaced += OnLayoutReplaced;
        // Building every panel takes a while; after the window's first frame, the loading overlay is on screen while it happens.
        Dispatcher.UIThread.Post(() => OnLayoutReplaced(this, EventArgs.Empty), DispatcherPriority.Background);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_shell is not null)
            _shell.Layout.LayoutReplaced -= OnLayoutReplaced;
        _shell = null;
    }

    private void OnLayoutReplaced(object? sender, EventArgs e)
    {
        if (_shell is not null)
            Host.Layout = _shell.Layout.Layout;
    }
}
