using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Editor.TileMaps.Controls;
using Talesmith.Editor.TileMaps.Panel;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Grids;
using AvaloniaColor = Avalonia.Media.Color;

namespace Talesmith.Editor.TileMaps.Dialogs;

/// <summary>The data a tile properties dialog saved; null <see cref="Info"/> clears the tile's data.</summary>
public sealed record TilePropertiesResult(TileInfo? Info);

/// <summary>A frame of a tile animation being edited.</summary>
public sealed partial class AnimationFrameViewModel(TilePropertiesDialogViewModel owner, TileOption tile, int duration) : ObservableObject
{
    public TileOption Tile { get; } = tile;

    [ObservableProperty]
    private double _duration = duration;

    [RelayCommand]
    private void Remove() => owner.RemoveFrame(this);
}

/// <summary>Edits one tile's name, color, custom properties, animation frames with a live preview, and collision polygons.</summary>
public sealed partial class TilePropertiesDialogViewModel : TileMapDialogViewModel, IDisposable
{
    private readonly Tileset _tileset;
    private readonly int _tileId;
    private readonly TileInfo? _original;
    private readonly DispatcherTimer _timer;
    private int _frame;
    private TimeSpan _elapsed;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _hasColor;

    [ObservableProperty]
    private AvaloniaColor _color;

    [ObservableProperty]
    private IImage? _previewImage;

    [ObservableProperty]
    private IBrush? _previewFill;

    /// <summary>0 shows the animation, 1 the collision shapes.</summary>
    [ObservableProperty]
    private int _sectionIndex;

    public TilePropertiesDialogViewModel(Tileset tileset, int tileId, IGridLayout layout)
    {
        _tileset = tileset;
        _tileId = tileId;
        _original = tileset.Find(tileId);
        Layout = layout;
        Tile = new TileOption(tileset, tileId);
        Tiles = TileOption.All(tileset);
        _name = _original?.Name ?? "";
        _hasColor = _original?.Color is not null || tileset.IsColorTileset;
        _color = TileArt.ToAvalonia(_original?.Color ?? (tileset.IsColorTileset ? Runtime.Maps.TileGeometry.FallbackColor(tileId) : Mathematics.Color.White));
        Properties = new PropertyListViewModel(_original?.Properties ?? PropertySet.Empty, _ => { });
        foreach (var frame in _original?.Animation ?? [])
        {
            if (Tiles.FirstOrDefault(t => t.Id == frame.TileId) is { } option)
                Frames.Add(new AnimationFrameViewModel(this, option, frame.DurationMilliseconds));
        }

        Collision = new CollisionShapes(_original?.Collision ?? []);
        ImageSize = new Size(tileset.TileWidth, tileset.TileHeight);
        Frames.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasFrames));
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Advance();
        ShowFrame(0);
        _timer.Start();
    }

    public IGridLayout Layout { get; }

    public TileOption Tile { get; }

    public IReadOnlyList<TileOption> Tiles { get; }

    public string Title => $"{_tileset.Name} · #{_tileId}";

    public string SizeText => $"{_tileset.TileWidth} × {_tileset.TileHeight} px";

    public PropertyListViewModel Properties { get; }

    public ObservableCollection<AnimationFrameViewModel> Frames { get; } = [];

    public bool HasFrames => Frames.Count > 0;

    public CollisionShapes Collision { get; }

    public Size ImageSize { get; }

    [RelayCommand]
    private void AddFrame(TileOption tile) => Frames.Add(new AnimationFrameViewModel(this, tile, Frames.Count > 0 ? (int)Frames[^1].Duration : 120));

    [RelayCommand]
    private void ClearCollision() => Collision.Clear();

    /// <summary>Adds a shape covering the whole cell.</summary>
    [RelayCommand]
    private void FillCell()
    {
        var corners = new Vector2[Layout.CornerCount];
        for (var i = 0; i < corners.Length; i++)
        {
            var corner = Layout.CornerOffset(i);
            corners[i] = new Vector2(MathF.Round(corner.X * 10) / 10, MathF.Round(corner.Y * 10) / 10);
        }

        Collision.Add(corners);
    }

    [RelayCommand]
    private void Save()
    {
        Collision.Finish();
        var info = new TileInfo(_tileId, string.IsNullOrWhiteSpace(Name) ? null : Name.Trim(), HasColor ? TileArt.FromAvalonia(Color) : null,
            [.. Frames.Select(f => new TileFrame(f.Tile.Id, Math.Max(1, (int)Math.Round(f.Duration))))], Properties.Properties)
        {
            Collision = Collision.Complete,
            FormatData = _original?.FormatData
        };
        Close(new TilePropertiesResult(info.HasData ? info : null));
    }

    [RelayCommand]
    private void Cancel() => Close(null);

    public void Dispose() => _timer.Stop();

    internal void RemoveFrame(AnimationFrameViewModel frame) => Frames.Remove(frame);

    private void Advance()
    {
        if (Frames.Count < 2)
        {
            ShowFrame(Frames.Count == 1 ? Frames[0].Tile.Id : _tileId);
            return;
        }

        _elapsed += _timer.Interval;
        _frame %= Frames.Count;
        if (_elapsed.TotalMilliseconds < Math.Max(1, Frames[_frame].Duration))
            return;
        _elapsed = TimeSpan.Zero;
        _frame = (_frame + 1) % Frames.Count;
        ShowFrame(Frames[_frame].Tile.Id);
    }

    private void ShowFrame(int tileId)
    {
        var option = Tiles.FirstOrDefault(t => t.Id == tileId) ?? Tile;
        if (!ReferenceEquals(PreviewImage, option.Image))
            PreviewImage = option.Image;
        PreviewFill = HasColor && _tileset.IsColorTileset ? new SolidColorBrush(Color) : option.Brush;
    }

    partial void OnColorChanged(AvaloniaColor value) => ShowFrame(_tileId);
}
