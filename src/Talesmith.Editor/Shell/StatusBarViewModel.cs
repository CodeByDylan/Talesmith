using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Editor.Documents;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Viewport;
using Talesmith.UI;

namespace Talesmith.Editor.Shell;

/// <summary>How a status bar item is colored.</summary>
public enum StatusKind
{
    Neutral,
    Success,
    Warning,
    Error,
    Busy
}

/// <summary>A piece of information in the status bar that a feature keeps up to date, such as the script compiler's state.</summary>
public sealed partial class StatusBarItem(string id, int order) : ObservableObject
{
    public string Id { get; } = id;

    /// <summary>The position among the items; lower comes first.</summary>
    public int Order { get; } = order;

    [ObservableProperty]
    private string? _text;

    [ObservableProperty]
    private Geometry? _icon;

    [ObservableProperty]
    private string? _toolTip;

    [ObservableProperty]
    private StatusKind _kind;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>Runs when the item is clicked; null makes the item plain text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClickable))]
    private ICommand? _command;

    public bool IsClickable => Command is not null;
}

/// <summary>The status bar: what the editor is doing, the open scene, the entity count, feature items, the cursor position, zoom and frame rate.</summary>
public sealed partial class StatusBarViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly IEditWorld _world;
    private readonly IPlayModeService _play;
    private readonly ViewportService _viewport;
    private readonly DispatcherTimer _playRate;
    private long _playFrames;
    private double _playFps;

    [ObservableProperty]
    private string _cursorText = "";

    public StatusBarViewModel(IProjectService project, ISceneDocumentService documents, IEditWorld world, IPlayModeService play, ViewportService viewport)
    {
        _project = project;
        _documents = documents;
        _world = world;
        _play = play;
        _viewport = viewport;
        project.StatusChanged += (_, _) => RefreshStatus();
        documents.StateChanged += (_, _) => RefreshStatus();
        documents.ActiveChanged += (_, _) => RefreshStatus();
        world.Changed += (_, _) => RefreshStatus();
        play.StateChanged += (_, _) => RefreshStatus();
        viewport.Camera.Changed += (_, _) => OnPropertyChanged(nameof(ZoomText));
        viewport.StatisticsChanged += (_, _) => OnPropertyChanged(nameof(FpsText));
        _playRate = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _playRate.Tick += (_, _) => SamplePlayRate();
        play.StateChanged += (_, _) => TrackPlayRate();
    }

    /// <summary>Items added by features, in order.</summary>
    public ObservableCollection<StatusBarItem> Items { get; } = [];

    public string StatusText => _project.ScanProgress is { } scan
        ? scan.Total > 0 ? $"Scanning assets {scan.Fraction:P0}" : "Scanning assets…"
        : _documents.IsLoading ? "Opening scene…"
        : !_project.IsReady ? (_project.WhenReady.IsFaulted ? "The project could not open" : "Opening project…")
        : _world.IsBusy ? "Loading scene…"
        : _play.State switch
        {
            PlayState.Starting => "Starting play mode…",
            PlayState.Playing => "Playing",
            PlayState.Paused => "Paused",
            _ => "Ready"
        };

    public StatusKind StatusKind => _project.WhenReady.IsFaulted ? StatusKind.Error
        : IsBusy ? StatusKind.Busy
        : _play.IsPlaying ? StatusKind.Warning
        : StatusKind.Success;

    public Geometry StatusIcon => StatusKind switch
    {
        StatusKind.Error => Icons.AlertCircle,
        StatusKind.Busy => Icons.Refresh,
        StatusKind.Warning => _play.IsPaused ? Icons.Pause : Icons.Play,
        _ => Icons.CheckCircle
    };

    public bool IsBusy => _project.ScanProgress is not null || _documents.IsLoading || !_project.IsReady && !_project.WhenReady.IsFaulted || _world.IsBusy
                          || _play.State == PlayState.Starting;

    /// <summary>The asset scan's progress from 0 to 1, or null when it is unknown.</summary>
    public double? Progress => _project.ScanProgress is { Total: > 0 } scan ? scan.Fraction : null;

    public string SceneText => _documents.Active?.FileName ?? "No scene";

    public string EntityText => _world.World is null ? "" : string.Create(CultureInfo.CurrentCulture, $"{_world.EntityCount:N0} {(_world.EntityCount == 1 ? "entity" : "entities")}");

    public string ZoomText => string.Create(CultureInfo.CurrentCulture, $"{_viewport.Camera.Zoom * 100:0.#}%");

    public string FpsText => _play.Game is not null ? string.Create(CultureInfo.CurrentCulture, $"{_playFps:0} fps")
        : _viewport.IsResting ? "Idle" : _viewport.FramesPerSecond <= 0 ? "– fps" : string.Create(CultureInfo.CurrentCulture, $"{_viewport.FramesPerSecond:0} fps");

    /// <summary>Adds an item, or returns the existing item with the id.</summary>
    public StatusBarItem AddItem(string id, int order = 0)
    {
        if (Items.FirstOrDefault(i => i.Id == id) is { } existing)
            return existing;
        var item = new StatusBarItem(id, order);
        var index = 0;
        while (index < Items.Count && Items[index].Order <= order)
            index++;
        Items.Insert(index, item);
        return item;
    }

    /// <summary>Shows the world position under the pointer, or clears it when the pointer left the viewport.</summary>
    public void SetCursor(Vector2? world) =>
        CursorText = world is { } w ? string.Create(CultureInfo.CurrentCulture, $"x {w.X:0}  y {w.Y:0}") : "";

    /// <summary>Stops sampling the play rate; a running timer is kept alive by the UI thread, and this view model with it.</summary>
    public void Dispose() => _playRate.Stop();

    private void RefreshStatus()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(SceneText));
        OnPropertyChanged(nameof(EntityText));
    }

    private void TrackPlayRate()
    {
        _playFrames = _play.Game?.FrameCount ?? 0;
        _playFps = 0;
        _playRate.IsEnabled = _play.Game is not null;
        OnPropertyChanged(nameof(FpsText));
    }

    private void SamplePlayRate()
    {
        if (_play.Game is not { } game)
            return;
        var frames = game.FrameCount;
        _playFps = (frames - _playFrames) / _playRate.Interval.TotalSeconds;
        _playFrames = frames;
        OnPropertyChanged(nameof(FpsText));
    }
}
