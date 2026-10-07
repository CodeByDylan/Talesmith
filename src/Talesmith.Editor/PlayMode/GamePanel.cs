using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Packs;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Loading;
using Talesmith.Avalonia.Presentation;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.PlayMode;

/// <summary>The Game panel: shows the play session with its overlays, framed in the accent color, and takes keyboard focus when play starts.</summary>
/// <remarks>
/// <para>From the moment play starts, the game's own loading screen covers the panel until the scene is shown, as in an exported game.</para>
/// <para>With the window preview on, the game runs in a window of the preview's size and display scale instead of filling the panel, shown at its
/// zoom with scroll bars when it does not fit; the window's outline shows while not playing.</para>
/// </remarks>
public sealed class GamePanel(IPlayModeService play, IProjectService project, EditorCommandRegistry commands, GameWindowPreview preview,
    ILogger<LoadingScreen> logger) : IEditorPanel, IDisposable
{
    private readonly GameWindowFrame _frame = new();
    private readonly ScrollViewer _scroller = new();
    private readonly Panel _area = new();
    private readonly Panel _stage = new();
    private IGamePresenter? _presenter;
    private GameHost? _host;
    private LoadingScreen? _loading;
    private EmptyState? _empty;

    public Control CreateContent()
    {
        _empty = new EmptyState
        {
            Icon = Icons.Play,
            Title = "Not playing",
            Hint = "Play runs the open scene here, with its unsaved changes. Stopping returns to editing exactly as you left it.",
            ActionText = "Play",
            ActionCommand = commands.Find("play.toggle")?.Command
        };
        _stage.Bind(Panel.BackgroundProperty, _stage.GetResourceObservable("BackgroundBrush"));
        _frame.Child = _stage;
        _frame.PropertyChanged += (_, e) =>
        {
            if (e.Property == GameWindowFrame.ActualZoomProperty)
                preview.ActualZoom = _frame.ActualZoom;
        };
        _scroller.Content = _frame;
        _area.Children.Add(_scroller);
        _area.Children.Add(_empty);
        var bar = new GameWindowBar { DataContext = preview };
        bar.Bind(Visual.IsVisibleProperty, new Binding(nameof(GameWindowPreview.IsEnabled)) { Source = preview });
        DockPanel.SetDock(bar, Dock.Top);
        play.StateChanged += (_, _) => Update();
        preview.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GameWindowPreview.IsEnabled) or nameof(GameWindowPreview.Size) or nameof(GameWindowPreview.Zoom))
                Update();
        };
        Update();
        return new DockPanel { Children = { bar, _area } };
    }

    public Control CreateHeaderActions()
    {
        var toggle = new ToggleButton { Classes = { "header" }, Content = new SymbolIcon { Data = Icons.MonitorSmartphone, Size = 14 } };
        ToolTip.SetTip(toggle, "Preview the game in a window of another size and display scale");
        toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(GameWindowPreview.IsEnabled)) { Source = preview, Mode = BindingMode.TwoWay });
        return toggle;
    }

    public void Dispose() => Detach();

    private void Update()
    {
        if (play.State == PlayState.Starting && _loading is null)
            ShowLoadingScreen();
        if (play.Session is { } session && _host is null)
            Attach(session);
        else if (play.Session is null && play.State != PlayState.Starting)
            Detach();
        var showing = _host is not null || _loading is not null;
        _frame.Window = preview.IsEnabled ? preview.Size : null;
        _frame.Zoom = preview.IsEnabled ? preview.Zoom.Factor : null;
        _frame.Bind(GameWindowFrame.RingBrushProperty, _frame.GetResourceObservable(!showing ? "BorderStrongBrush" : play.IsPaused ? "WarningBrush" : "AccentBrush"));
        _frame.IsVisible = showing || preview.IsEnabled;
        var scrolls = preview.IsEnabled && preview.Zoom.Factor is not null ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        _scroller.HorizontalScrollBarVisibility = scrolls;
        _scroller.VerticalScrollBarVisibility = scrolls;
        if (preview.IsEnabled)
            _area.Bind(Panel.BackgroundProperty, _area.GetResourceObservable("SurfaceSunkenBrush"));
        else
            _area.ClearValue(Panel.BackgroundProperty);
        if (_empty is not null)
            _empty.IsVisible = !showing;
    }

    private void ShowLoadingScreen()
    {
        _loading = new LoadingScreen(project.Settings, OpenAssets(), logger);
        _stage.Children.Add(_loading);
    }

    private void Attach(GameSession session)
    {
        if (_loading is null)
            ShowLoadingScreen();
        _presenter = session.Backend.CreatePresenter(session.Game);
        _host = new GameHost(session.Game, _presenter, project.Project.GetStatePath("captures"));
        _stage.Children.Insert(0, _host);
        _loading!.Follow(session.Game);
        Dispatcher.UIThread.Post(() => _host?.View.Focus(), DispatcherPriority.Background);
    }

    private void Detach()
    {
        _stage.Children.Clear();
        _loading = null;
        if (_host is null)
            return;
        _host = null;
        _presenter?.Dispose();
        _presenter = null;
    }

    /// <summary>The project's assets, for the loading screen's image; null when the folder cannot be read.</summary>
    private IAssetSource? OpenAssets()
    {
        try
        {
            return PackAssetSource.OpenFolder(project.Project.AssetRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or AssetException)
        {
            return null;
        }
    }
}
