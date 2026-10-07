using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Projects;
using Talesmith.UI;

namespace Talesmith.Editor.PlayMode;

/// <summary>The Game panel's window preview: whether the game runs in a window of a chosen size and display scale rather than filling the panel,
/// and how large the panel shows that window. The project keeps it.</summary>
public sealed partial class GameWindowPreview : ObservableObject, IEditorCommandContributor, IDisposable
{
    private const string StateKey = "game.window";

    private readonly IProjectService _project;
    private readonly ProjectState _state;
    private bool _loading;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Width), nameof(Height), nameof(DisplayScale), nameof(AspectRatio), nameof(Preset))]
    private GameWindowSize _size = new(1920, 1080);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FittedZoomText))]
    private GameWindowZoom _zoom = GameWindowZoom.Fit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FittedZoomText))]
    private double _actualZoom = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Preset))]
    private IReadOnlyList<GameWindowPreset> _presets = [];

    public GameWindowPreview(IProjectService project, ProjectState state)
    {
        _project = project;
        _state = state;
        Presets = CreatePresets();
        Load();
        project.StatusChanged += OnProjectStatusChanged;
    }

    /// <summary>The window's width in device pixels, between <see cref="GameWindowSize.MinimumSide"/> and
    /// <see cref="GameWindowSize.MaximumSide"/>.</summary>
    public int Width
    {
        get => Size.Width;
        set => Size = (Size with { Width = value }).Clamped();
    }

    public int Height
    {
        get => Size.Height;
        set => Size = (Size with { Height = value }).Clamped();
    }

    /// <summary>Device pixels per logical pixel of the window's display, one of <see cref="DisplayScales"/>.</summary>
    public double DisplayScale
    {
        get => Size.DisplayScale;
        set => Size = (Size with { DisplayScale = value }).Clamped();
    }

    public IReadOnlyList<double> DisplayScales { get; } = GameWindowSize.DisplayScales;

    public IReadOnlyList<GameWindowZoom> Zooms { get; } = GameWindowZoom.All;

    public string AspectRatio => Size.AspectRatio;

    /// <summary>The preset with the window's size, also turned a quarter, or null for a size of its own; choosing one takes its size.</summary>
    public GameWindowPreset? Preset
    {
        get => Presets.FirstOrDefault(p => p.Size == Size) ?? Presets.FirstOrDefault(p => p.Size.Rotated() == Size);
        set
        {
            if (value is not null && value != Preset)
                Size = value.Size;
        }
    }

    /// <summary>The zoom the window fits at, such as "37%", while it fits; empty at a chosen zoom, which shows its own.</summary>
    public string FittedZoomText => Zoom.Factor is null ? GameWindowZoom.Percent(ActualZoom) : "";

    public void Dispose() => _project.StatusChanged -= OnProjectStatusChanged;

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        builder.Add("play.windowPreview", "Toggle window preview", "Play", () => IsEnabled = !IsEnabled, null, null, Icons.MonitorSmartphone,
            "Runs the game in the Game panel in a window of another size and display scale, such as 4K or a phone, or fills the panel again.");
        builder.Menu(MenuPaths.Scene, "play.windowPreview", "play");
    }

    [RelayCommand]
    private void Rotate() => Size = Size.Rotated();

    [RelayCommand]
    private void Close() => IsEnabled = false;

    partial void OnIsEnabledChanged(bool value) => Save();

    partial void OnSizeChanged(GameWindowSize value) => Save();

    partial void OnZoomChanged(GameWindowZoom value) => Save();

    private List<GameWindowPreset> CreatePresets() => [.. GameWindowPreset.Of(_project.Settings), .. GameWindowPreset.Common];

    private void OnProjectStatusChanged(object? sender, EventArgs e)
    {
        var presets = CreatePresets();
        if (!presets.SequenceEqual(Presets))
            Presets = presets;
    }

    private void Load()
    {
        if (_state.Get<SavedWindow>(StateKey) is not { } saved)
            return;
        _loading = true;
        Size = new GameWindowSize(saved.Width, saved.Height, saved.DisplayScale).Clamped();
        Zoom = GameWindowZoom.All.FirstOrDefault(z => z.Factor == saved.Zoom) ?? GameWindowZoom.Fit;
        IsEnabled = saved.Enabled;
        _loading = false;
    }

    private void Save()
    {
        if (!_loading)
            _state.Set(StateKey, new SavedWindow(IsEnabled, Size.Width, Size.Height, Size.DisplayScale, Zoom.Factor));
    }

    private sealed record SavedWindow(bool Enabled, int Width, int Height, double DisplayScale, double? Zoom);
}
