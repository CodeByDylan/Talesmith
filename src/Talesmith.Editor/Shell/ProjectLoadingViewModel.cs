using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hub;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Viewport;
using Talesmith.Runtime.Hosting;
using Talesmith.UI;

namespace Talesmith.Editor.Shell;

/// <summary>Where a step of opening a project stands.</summary>
public enum LoadingStepState
{
    Pending,
    Active,
    Done,
    Failed
}

/// <summary>One step of opening a project, such as scanning its assets, with its progress while it runs.</summary>
public sealed partial class LoadingStepViewModel(string title) : ObservableObject
{
    [ObservableProperty]
    private string _title = title;

    /// <summary>How far the step has come in words, such as "120 of 480", or null.</summary>
    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon), nameof(IsActive), nameof(IsPending), nameof(IsDone), nameof(IsFailed))]
    private LoadingStepState _state;

    /// <summary>The step's progress from 0 to 1, or null while it cannot tell.</summary>
    [ObservableProperty]
    private double? _progress;

    public bool IsPending => State == LoadingStepState.Pending;

    public bool IsActive => State == LoadingStepState.Active;

    public bool IsDone => State == LoadingStepState.Done;

    public bool IsFailed => State == LoadingStepState.Failed;

    public Geometry Icon => State switch
    {
        LoadingStepState.Done => Icons.CheckCircle,
        LoadingStepState.Failed => Icons.AlertCircle,
        _ => Icons.Circle
    };
}

/// <summary>Covers the editor's workspace while the project opens: the project's picture and name over the steps from starting the engine
/// to showing the scene, each with its progress. It closes once the scene is in the viewport, and shows what went wrong when the project
/// could not open.</summary>
public sealed partial class ProjectLoadingViewModel : ObservableObject, IDisposable
{
    /// <summary>Frames the scene view draws with its camera on the loaded scene before the overlay goes, so it never uncovers an empty view.</summary>
    private const int ShownFrames = 3;

    /// <summary>How long the overlay waits for the scene view, which draws nothing when the layout has no Scene panel.</summary>
    private static readonly TimeSpan LongestWaitForSceneView = TimeSpan.FromSeconds(3);

    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly IEditWorld _world;
    private readonly LayoutService _layout;
    private readonly ViewportService _viewport;
    private readonly DispatcherTimer _poll;
    private bool _refreshQueued;

    /// <summary>Whether the overlay is up; it goes down once the project has opened, or when the user dismisses an error.</summary>
    [ObservableProperty]
    private bool _isOpen = true;

    /// <summary>Why the project could not open, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailed), nameof(IsLoading))]
    private string? _error;

    public ProjectLoadingViewModel(IProjectService project, ISceneDocumentService documents, IEditWorld world, LayoutService layout,
        ViewportService viewport)
    {
        _project = project;
        _documents = documents;
        _world = world;
        _layout = layout;
        _viewport = viewport;
        var details = project.Project;
        Name = details.Name;
        DisplayPath = ProjectArtwork.DisplayPath(details.Folder);
        Initials = ProjectArtwork.Initials(details.Name);
        Artwork = ProjectArtwork.Gradient(details.Name);
        Thumbnail = ProjectArtwork.LoadThumbnail(details.Folder);
        Steps = [Engine, Assets, Scene];
        _poll = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) => Refresh();
        project.StatusChanged += OnChanged;
        documents.ActiveChanged += OnChanged;
        documents.StateChanged += OnChanged;
        world.Changed += OnChanged;
        Refresh();
    }

    public string Name { get; }

    public string DisplayPath { get; }

    public string Initials { get; }

    public IBrush Artwork { get; }

    /// <summary>The scene view saved when the project last closed, or null for a project opened for the first time.</summary>
    public Bitmap? Thumbnail { get; }

    public bool HasThumbnail => Thumbnail is not null;

    public LoadingStepViewModel Engine { get; } = new("Starting the game engine");

    public LoadingStepViewModel Assets { get; } = new("Scanning assets");

    public LoadingStepViewModel Scene { get; } = new("Loading the scene");

    public IReadOnlyList<LoadingStepViewModel> Steps { get; }

    public bool HasFailed => Error is not null;

    public bool IsLoading => Error is null;

    public void Dispose()
    {
        _poll.Stop();
        _project.StatusChanged -= OnChanged;
        _documents.ActiveChanged -= OnChanged;
        _documents.StateChanged -= OnChanged;
        _world.Changed -= OnChanged;
    }

    [RelayCommand]
    private async Task CloseProjectAsync() => await _project.CloseAsync();

    /// <summary>Takes the overlay down after an error, so the console can tell more.</summary>
    [RelayCommand]
    private void ShowConsole()
    {
        IsOpen = false;
        _layout.ShowPanel(PanelIds.Console);
    }

    /// <summary>Refreshes once the current event has reached every listener, so the edit world has picked up a newly opened scene first.</summary>
    private void OnChanged(object? sender, EventArgs e)
    {
        if (_refreshQueued || !IsOpen)
            return;
        _refreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshQueued = false;
            Refresh();
        }, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        if (!IsOpen || HasFailed || Scene.IsDone)
            return;
        if (_project.WhenReady.IsFaulted)
        {
            Fail(_project.WhenReady.Exception!.GetBaseException().Message);
            return;
        }

        var engineStarted = _project.EditSession is not null;
        Engine.State = engineStarted ? LoadingStepState.Done : LoadingStepState.Active;
        RefreshScan(engineStarted);
        if (!_project.IsReady)
        {
            Scene.State = LoadingStepState.Pending;
            return;
        }

        var scene = _documents.Active;
        Scene.Title = scene is null ? "Loading the scene" : $"Loading {scene.FileName}";
        if (scene is not null && !_documents.IsLoading && !_world.IsBusy)
        {
            Finish();
            return;
        }

        Scene.State = LoadingStepState.Active;
        var progress = _world.Game?.Scenes.LoadProgress;
        Scene.Progress = progress?.Fraction;
        Scene.Detail = progress is { Total: > 0 } ? Count(progress.Loaded, progress.Total) : null;
        _poll.Start();
    }

    private void RefreshScan(bool engineStarted)
    {
        if (_project.IsReady)
        {
            if (Assets.IsDone)
                return;
            Assets.State = LoadingStepState.Done;
            Assets.Progress = 1;
            Assets.Detail = _project.Database is { } database ? Count(database.Assets.Count(a => !a.IsFolder), null) : null;
        }
        else if (_project.ScanProgress is { Total: > 0 } scan)
        {
            Assets.State = LoadingStepState.Active;
            Assets.Progress = scan.Fraction;
            Assets.Detail = Count(scan.Processed, scan.Total);
        }
        else
        {
            Assets.State = engineStarted ? LoadingStepState.Active : LoadingStepState.Pending;
            Assets.Progress = null;
        }
    }

    private async void Finish()
    {
        _poll.Stop();
        Scene.State = LoadingStepState.Done;
        Scene.Progress = 1;
        await Task.WhenAny(SceneShownAsync(), Task.Delay(LongestWaitForSceneView));
        IsOpen = false;
    }

    /// <summary>Completes once the scene view has its camera on the scene and the frames that show it have reached the screen.</summary>
    private async Task SceneShownAsync()
    {
        await CameraPlacedAsync();
        if (_world.Game is { } game)
            await FramesAsync(game, ShownFrames);
        if (Compositor.TryGetDefaultCompositor() is { } compositor)
            await compositor.RequestCompositionBatchCommitAsync().Rendered;
    }

    private Task CameraPlacedAsync()
    {
        if (_viewport.HasPlacedCamera)
            return Task.CompletedTask;
        var placed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _viewport.CameraPlaced += OnPlaced;
        return placed.Task;

        void OnPlaced(object? sender, EventArgs e)
        {
            _viewport.CameraPlaced -= OnPlaced;
            placed.TrySetResult();
        }
    }

    /// <summary>Completes once the game has published <paramref name="count"/> more frames.</summary>
    private static Task FramesAsync(Game game, int count)
    {
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remaining = count;
        game.FramePublished += OnFrame;
        return published.Task;

        void OnFrame()
        {
            if (Interlocked.Decrement(ref remaining) > 0)
                return;
            game.FramePublished -= OnFrame;
            published.TrySetResult();
        }
    }

    private void Fail(string message)
    {
        _poll.Stop();
        foreach (var step in Steps.Where(s => s.IsActive))
            step.State = LoadingStepState.Failed;
        Error = message;
    }

    private static string Count(int done, int? total) => total is { } all
        ? string.Create(CultureInfo.CurrentCulture, $"{done:N0} of {all:N0}")
        : string.Create(CultureInfo.CurrentCulture, $"{done:N0} assets");
}
