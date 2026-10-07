using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Editor.Settings;

namespace Talesmith.Editor.Viewport;

/// <summary>Where transform handles sit for a selection of several entities.</summary>
public enum PivotMode
{
    /// <summary>At the primary entity's position.</summary>
    Pivot,

    /// <summary>At the center of the selection's bounds.</summary>
    Center
}

/// <summary>Whether transform handles follow the entity's rotation.</summary>
public enum HandleSpace
{
    Local,
    Global
}

/// <summary>The viewport's display and snapping options shown in the toolbar; grid and snapping defaults persist in the settings.</summary>
public sealed partial class ViewportOptions : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;

    [ObservableProperty]
    private bool _showGrid;

    /// <summary>Whether the grid of a tool with one of its own shows while that tool is active, such as the edited map's cells for tile tools.</summary>
    [ObservableProperty]
    private bool _showTileGrid;

    [ObservableProperty]
    private bool _snapToGrid;

    [ObservableProperty]
    private double _gridSize;

    [ObservableProperty]
    private double _rotationSnapDegrees;

    [ObservableProperty]
    private bool _showIcons;

    [ObservableProperty]
    private PivotMode _pivot = PivotMode.Pivot;

    [ObservableProperty]
    private HandleSpace _space = HandleSpace.Global;

    /// <summary>Whether animations, particles and lights run in the viewport (<c>ExecutionModes.Preview</c>).</summary>
    [ObservableProperty]
    private bool _isPreviewing;

    public ViewportOptions(ISettingsService settings)
    {
        _settings = settings;
        var current = settings.Current;
        _showGrid = current.ShowGrid;
        _showTileGrid = current.ShowTileGrid;
        _snapToGrid = current.SnapToGrid;
        _gridSize = current.GridSize;
        _rotationSnapDegrees = current.RotationSnapDegrees;
        _showIcons = current.ShowEntityIcons;
        settings.Changed += OnSettingsChanged;
    }

    private bool _syncing;

    /// <summary>Stops following the settings, which outlive the project and would otherwise keep the viewport alive.</summary>
    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private void OnSettingsChanged(object? sender, EventArgs e) => Sync();

    private void Sync()
    {
        var current = _settings.Current;
        _syncing = true;
        ShowGrid = current.ShowGrid;
        ShowTileGrid = current.ShowTileGrid;
        SnapToGrid = current.SnapToGrid;
        GridSize = current.GridSize;
        RotationSnapDegrees = current.RotationSnapDegrees;
        ShowIcons = current.ShowEntityIcons;
        _syncing = false;
    }

    private void Save(Action<EditorSettings> change)
    {
        if (!_syncing)
            _settings.Update(change);
    }

    public bool IsLocal
    {
        get => Space == HandleSpace.Local;
        set => Space = value ? HandleSpace.Local : HandleSpace.Global;
    }

    public bool IsCenterPivot
    {
        get => Pivot == PivotMode.Center;
        set => Pivot = value ? PivotMode.Center : PivotMode.Pivot;
    }

    /// <summary>Rounds a world position to the grid when snapping is on, or always when <paramref name="force"/> is set, such as while Ctrl is held.</summary>
    public Vector2 Snap(Vector2 world, bool force = false)
    {
        if (!(SnapToGrid || force) || GridSize <= 0)
            return world;
        var size = (float)GridSize;
        return new Vector2(MathF.Round(world.X / size) * size, MathF.Round(world.Y / size) * size);
    }

    /// <summary>Rounds an angle in degrees to the rotation snap when snapping is on.</summary>
    public float SnapAngle(float degrees, bool force = false)
    {
        if (!(SnapToGrid || force) || RotationSnapDegrees <= 0)
            return degrees;
        var step = (float)RotationSnapDegrees;
        return MathF.Round(degrees / step) * step;
    }

    partial void OnShowGridChanged(bool value) => Save(s => s.ShowGrid = value);

    partial void OnShowTileGridChanged(bool value) => Save(s => s.ShowTileGrid = value);

    partial void OnSnapToGridChanged(bool value) => Save(s => s.SnapToGrid = value);

    partial void OnGridSizeChanged(double value)
    {
        if (value > 0)
            Save(s => s.GridSize = value);
    }

    partial void OnRotationSnapDegreesChanged(double value) => Save(s => s.RotationSnapDegrees = value);

    partial void OnShowIconsChanged(bool value) => Save(s => s.ShowEntityIcons = value);

    partial void OnSpaceChanged(HandleSpace value) => OnPropertyChanged(nameof(IsLocal));

    partial void OnPivotChanged(PivotMode value) => OnPropertyChanged(nameof(IsCenterPivot));
}
