using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Viewport;

/// <summary>The Scene panel: the edit game's view with the editor overlay, the tool rail and the zoom controls.</summary>
public sealed partial class SceneViewportPanel : ObservableObject, IEditorPanel, IDisposable
{
    private readonly IProjectService _project;
    private readonly IEditWorld _world;
    private readonly ISelectionService _selection;
    private readonly ISceneDocumentService _documents;
    private readonly StatusBarViewModel _status;
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _closing = new();
    private SceneViewportView? _view;
    private IGamePresenter? _presenter;
    private GameView? _gameView;

    public SceneViewportPanel(IProjectService project, IEditWorld world, ISelectionService selection, ISceneDocumentService documents, ToolManager tools,
        ViewportService viewport, ViewportGrid grid, EditorCommandRegistry commands, StatusBarViewModel status)
    {
        _project = project;
        _world = world;
        _selection = selection;
        _documents = documents;
        _status = status;
        Tools = tools;
        Viewport = viewport;
        Grid = grid;
        Commands = commands;
        project.StatusChanged += (_, _) =>
        {
            AttachGame();
            OnPropertyChanged(nameof(IsOpening));
        };
        project.EditSessionChanged += OnEditSessionChanged;
        world.Changed += (_, _) => OnPropertyChanged(nameof(IsBusy));
        viewport.Camera.Changed += (_, _) => OnPropertyChanged(nameof(ZoomText));
        viewport.Options.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsPreviewing));
    }

    public ToolManager Tools { get; }

    public ViewportService Viewport { get; }

    /// <summary>The grid the viewport shows, which the header's grid toggle shows or hides.</summary>
    public ViewportGrid Grid { get; }

    public EditorCommandRegistry Commands { get; }

    public EditorCommand this[string id] => Commands.Get(id);

    /// <summary>Whether the edit game does not exist yet.</summary>
    public bool IsOpening => _project.EditSession is null;

    /// <summary>Whether the scene is loading into the viewport or waiting for assets.</summary>
    public bool IsBusy => _world.IsBusy;

    public bool IsPreviewing => Viewport.Options.IsPreviewing;

    public string ZoomText => $"{Viewport.Camera.Zoom * 100:0.#}%";

    public Control CreateContent()
    {
        _view = new SceneViewportView { DataContext = this };
        var overlay = new ViewportOverlay(Viewport, Tools, _selection, _documents);
        overlay.PointerMovedOverView += (_, _) => _status.SetCursor(overlay.PointerWorld);
        _view.SetOverlay(overlay);
        AttachGame();
        return _view;
    }

    public Control? CreateHeaderActions() => new SceneViewportActions { DataContext = this };

    /// <summary>Captures the next frame of the viewport, or returns null when none arrives in time.</summary>
    public async Task<Imaging.ImageData?> CaptureAsync(TimeSpan timeout)
    {
        if (_presenter is not { } presenter)
            return null;
        Viewport.Wake();
        var capture = presenter.CaptureAsync();
        if (await Task.WhenAny(capture, Task.Delay(timeout)) != capture)
            return null;
        try
        {
            return await capture;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _closing.Cancel();
        _presenter?.Dispose();
        _presenter = null;
    }

    private void AttachGame()
    {
        if (_view is null || _presenter is not null || _project.EditSession is not { } session)
            return;
        Show(session);
    }

    private void Show(GameSession session)
    {
        _presenter = session.Backend.CreatePresenter(session.Game);
        _gameView = new GameView(session.Game, _presenter);
        _view!.SetGameView(_gameView);
        Viewport.Wake();
    }

    /// <summary>Shows the new edit game below the old one, which stays on top until the new game shows the scene.</summary>
    private void OnEditSessionChanged(object? sender, EditSessionChangedEventArgs e)
    {
        if (_view is null || _presenter is not { } presenter || _gameView is not { } shown)
            return;
        Show(e.Current);
        e.KeepPrevious(HandOverAsync(e.Previous.Game, shown, presenter));
    }

    private async Task HandOverAsync(Game previous, GameView shown, IGamePresenter presenter)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Check(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(_world.Game, previous) && !_world.IsBusy)
                ready.TrySetResult();
        }

        _world.Changed += Check;
        Check(null, EventArgs.Empty);
        await Task.WhenAny(ready.Task, Task.Delay(HandOverTimeout, _closing.Token));
        _world.Changed -= Check;
        _view?.RemoveGameView(shown);
        presenter.Dispose();
    }
}
