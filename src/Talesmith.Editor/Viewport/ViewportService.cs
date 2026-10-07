using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Settings;
using Talesmith.Mathematics;
using Talesmith.Runtime.Hosting;
using Talesmith.Systems;
using Talesmith.UI;

namespace Talesmith.Editor.Viewport;

/// <summary>Connects the viewport's camera and options to the edit game: draws frames through the editor camera, renders only while
/// something changes, switches preview, remembers the camera of each scene and offers the view commands.</summary>
public sealed class ViewportService : IEditorCommandContributor, IDisposable
{
    private const string CamerasKey = "viewport.cameras";
    private const int IdleFramesPerSecond = 4;
    private static readonly TimeSpan ActiveTime = TimeSpan.FromMilliseconds(600);
    private static readonly float[] ZoomPresets = [0.125f, 0.25f, 0.5f, 1, 2, 3, 4, 6, 8, 12, 16];

    private readonly ISettingsService _settings;
    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly IEditWorld _world;
    private readonly ISelectionService _selection;
    private readonly ProjectState _state;
    private readonly DispatcherTimer _idleTimer;
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();
    private Dictionary<string, CameraState> _cameras;
    private Game? _game;
    private int _frames;
    private bool _active;
    private bool _framedOnLoad;

    public ViewportService(IProjectService project, ISceneDocumentService documents, IEditWorld world, ISelectionService selection, ProjectState state,
        ViewportCamera camera, ViewportOptions options, EntityPicker picker, ISettingsService settings)
    {
        _settings = settings;
        _project = project;
        _documents = documents;
        _world = world;
        _selection = selection;
        _state = state;
        Camera = camera;
        Options = options;
        Picker = picker;
        _cameras = state.Get<Dictionary<string, CameraState>>(CamerasKey) ?? [];
        _idleTimer = new DispatcherTimer { Interval = ActiveTime };
        _idleTimer.Tick += (_, _) => GoIdle();

        Camera.Changed += (_, _) =>
        {
            ApplyCamera();
            Wake();
            Invalidated?.Invoke(this, EventArgs.Empty);
        };
        Options.PropertyChanged += OnOptionsChanged;
        _project.StatusChanged += (_, _) => AttachGame();
        _project.EditSessionChanged += (_, e) => Attach(e.Current.Game);
        _documents.ActiveChanged += OnDocumentChanged;
        _world.Changed += (_, _) =>
        {
            Wake();
            if (!_framedOnLoad && _world.World is not null && !_world.IsBusy)
                RestoreCamera();
            Invalidated?.Invoke(this, EventArgs.Empty);
        };
        _selection.Changed += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
        AttachGame();
    }

    public ViewportCamera Camera { get; }

    public ViewportOptions Options { get; }

    public EntityPicker Picker { get; }

    /// <summary>The zoom factor per wheel notch.</summary>
    public double ZoomStep => _settings.Current.ZoomStep;

    public bool SmoothZoom => _settings.Current.SmoothZoom;

    /// <summary>The game's background color, used when the scene sets none.</summary>
    public Color ClearColor => _project.Settings.ClearColor;

    /// <summary>The edit game's frames per second over the last second; low while nothing changes, since the view then rests.</summary>
    public double FramesPerSecond { get; private set; }

    /// <summary>Whether the view rests, drawing only a few frames a second because nothing changes.</summary>
    public bool IsResting => !_active;

    /// <summary>Whether the camera shows the open scene: restored to where it was last time, or framed around the scene once it loaded.</summary>
    public bool HasPlacedCamera => _framedOnLoad;

    /// <summary>Raised when the camera was placed on a newly loaded scene; see <see cref="HasPlacedCamera"/>.</summary>
    public event EventHandler? CameraPlaced;

    /// <summary>Raised when the overlay should be drawn again.</summary>
    public event EventHandler? Invalidated;

    /// <summary>Raised about once a second with a new <see cref="FramesPerSecond"/>.</summary>
    public event EventHandler? StatisticsChanged;

    /// <summary>Renders at the display's rate for a moment, such as while the user interacts; afterwards frames slow down to save power.</summary>
    public void Wake()
    {
        if (_game is null)
            return;
        if (!_active)
        {
            _active = true;
            var pacing = _game.Services.GetRequiredService<FramePacing>();
            pacing.MaxFramesPerSecond = 0;
            pacing.VSync = true;
            _frames = 0;
            FramesPerSecond = 0;
            _fpsClock.Restart();
            StatisticsChanged?.Invoke(this, EventArgs.Empty);
        }

        _idleTimer.Stop();
        _idleTimer.Start();
    }

    public void FrameSelection(bool animate = true)
    {
        var bounds = _selection.Entities.Count > 0 ? Picker.GetBounds(_selection.Entities, Camera.Zoom) : null;
        if (bounds is { } b)
            Camera.Frame(b.Inflate(Math.Max(b.Width, b.Height) * 0.15f + 8), animate);
        else
            FrameAll(animate);
    }

    public void FrameAll(bool animate = true)
    {
        if (Picker.GetSceneBounds(Camera.Zoom) is { } bounds)
            Camera.Frame(bounds, animate);
        else
            Camera.AnimateTo(Vector2.Zero, 1);
    }

    /// <summary>Steps through the zoom presets: 1 zooms in, -1 out.</summary>
    public void StepZoom(int direction)
    {
        var current = Camera.Zoom;
        var next = direction > 0 ? ZoomPresets.FirstOrDefault(z => z > current * 1.01f, ZoomPresets[^1]) : ZoomPresets.LastOrDefault(z => z < current * 0.99f, ZoomPresets[0]);
        Camera.ZoomTo(next, animate: true);
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string View = "View";
        builder.Add("view.frameSelection", "Frame selection", View, () => FrameSelection(), null, "F", Icons.Crosshair, "Centers the view on the selected entities.");
        builder.Add("view.frameAll", "Frame scene", View, () => FrameAll(), null, "Shift+F", Icons.Maximize, "Fits the whole scene in the view.");
        builder.Add("view.zoomIn", "Zoom in", View, () => StepZoom(1), null, "Ctrl+OemPlus", Icons.ZoomIn);
        builder.Add("view.zoomOut", "Zoom out", View, () => StepZoom(-1), null, "Ctrl+OemMinus", Icons.ZoomOut);
        builder.Add("view.actualSize", "Actual size (100%)", View, () => Camera.ZoomTo(1, animate: true), null, "Ctrl+D1", Icons.Maximize2);
        builder.Add("view.origin", "Go to origin", View, () => Camera.AnimateTo(Vector2.Zero, Camera.Zoom), null, "Home", Icons.Crosshair);
        builder.Add("view.snap", "Toggle snapping", View, () => Options.SnapToGrid = !Options.SnapToGrid, null, "Ctrl+Shift+G", Icons.Magnet);
        builder.Add("view.icons", "Toggle entity icons", View, () => Options.ShowIcons = !Options.ShowIcons, null, null, Icons.Eye);
        builder.Add("view.preview", "Toggle preview", View, () => Options.IsPreviewing = !Options.IsPreviewing, null, "Ctrl+Alt+P", Icons.Sparkles,
            "Runs animations, particles and lights in the viewport without running gameplay.");
        builder.Add("view.pivot", "Toggle pivot or center handles", View, () => Options.IsCenterPivot = !Options.IsCenterPivot, null, "Z", Icons.Crosshair);
        builder.Add("view.space", "Toggle local or global handles", View, () => Options.IsLocal = !Options.IsLocal, null, "X", Icons.Rotate3D);

        foreach (var id in new[] { "view.frameSelection", "view.frameAll", "view.zoomIn", "view.zoomOut", "view.actualSize", "view.origin" })
            builder.Menu(MenuPaths.Scene + "/View", id, "navigate");
        foreach (var id in new[] { "view.snap", "view.icons", "view.preview" })
            builder.Menu(MenuPaths.Scene + "/View", id, "display");
    }

    public void Dispose()
    {
        SaveCamera();
        _idleTimer.Stop();
        if (_game is not null)
            _game.FramePublished -= OnFramePublished;
    }

    private void AttachGame()
    {
        if (_game is null && _project.EditSession?.Game is { } game)
            Attach(game);
    }

    private void Attach(Game game)
    {
        if (_game is not null)
            _game.FramePublished -= OnFramePublished;
        _game = game;
        _active = false;
        game.FramePublished += OnFramePublished;
        ApplyCamera();
        ApplyMode();
        Wake();
    }

    private void ApplyCamera()
    {
        var camera = Camera.ToCamera2D();
        if (_game is not null)
            _game.CameraOverride = camera;
        if (_world.Game is { } shown && !ReferenceEquals(shown, _game))
            shown.CameraOverride = camera;
    }

    private void ApplyMode()
    {
        if (_game is null)
            return;
        _game.Mode = Options.IsPreviewing ? ExecutionModes.Preview : ExecutionModes.Edit;
        if (Options.IsPreviewing)
            Wake();
    }

    private void GoIdle()
    {
        _idleTimer.Stop();
        if (_game is null || Options.IsPreviewing || Camera.IsAnimating || _world.IsBusy)
        {
            _idleTimer.Start();
            return;
        }

        _active = false;
        SaveCamera();
        var pacing = _game.Services.GetRequiredService<FramePacing>();
        pacing.VSync = false;
        pacing.MaxFramesPerSecond = IdleFramesPerSecond;
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnFramePublished()
    {
        _frames++;
        var elapsed = _fpsClock.Elapsed.TotalSeconds;
        if (elapsed < 1)
            return;
        FramesPerSecond = _frames / elapsed;
        _frames = 0;
        _fpsClock.Restart();
        StatisticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewportOptions.IsPreviewing))
            ApplyMode();
        Wake();
        Invalidated?.Invoke(this, EventArgs.Empty);
    }

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        SaveCamera();
        _framedOnLoad = false;
        _selection.Clear();
    }

    private void RestoreCamera()
    {
        _framedOnLoad = true;
        if (_documents.Active?.Path is { } path && _cameras.TryGetValue(path, out var saved))
            Camera.Set(new Vector2(saved.X, saved.Y), saved.Zoom);
        else if (Camera.ViewSize.Width > 0)
            FrameAll(animate: false);
        else
            _framedOnLoad = false;
        if (_framedOnLoad)
            CameraPlaced?.Invoke(this, EventArgs.Empty);
    }

    private void SaveCamera()
    {
        if (_documents.Active?.Path is not { } path || !_framedOnLoad)
            return;
        _cameras[path] = new CameraState(Camera.Position.X, Camera.Position.Y, Camera.Zoom);
        _state.Set(CamerasKey, _cameras);
    }

    /// <summary>Frames the scene once the viewport has a size, if the camera was not restored yet.</summary>
    internal void OnViewSized()
    {
        if (!_framedOnLoad && _world.World is not null && !_world.IsBusy)
            RestoreCamera();
    }

    /// <summary>Remembers the camera of the open scene, such as when the editor closes.</summary>
    internal void Persist() => SaveCamera();

    private sealed record CameraState(float X, float Y, float Zoom);
}
