using System.Numerics;
using Talesmith.Assets.Textures;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Maps;

/// <summary>One frame of an animated tile.</summary>
public readonly record struct TileFrame(int TileId, int DurationMilliseconds);

/// <summary>Data attached to a single tile of a tileset.</summary>
/// <param name="Color">The tile's flat color, used for color tilesets and as a fallback when artwork is missing.</param>
public sealed record TileInfo(int Id, string? Name, Color? Color, IReadOnlyList<TileFrame> Animation, PropertySet Properties)
{
    /// <summary>Solid polygons relative to the cell center; empty means the whole cell is solid on collision layers.</summary>
    public IReadOnlyList<IReadOnlyList<Vector2>> Collision { get; init; } = [];

    /// <summary>Data the format the tile was read from keeps to write it back faithfully; editors leave it alone.</summary>
    public object? FormatData { get; init; }

    public bool IsAnimated => Animation.Count > 1;

    /// <summary>Whether the tile carries anything worth storing; tiles without data are dropped from their tileset.</summary>
    public bool HasData => !string.IsNullOrEmpty(Name) || Color.HasValue || Animation.Count > 0 || Properties.Count > 0 || Collision.Count > 0;

    /// <summary>The total length of one animation cycle in milliseconds.</summary>
    public int AnimationLength
    {
        get
        {
            var length = 0;
            foreach (var frame in Animation)
                length += Math.Max(1, frame.DurationMilliseconds);
            return length;
        }
    }

    /// <summary>Creates the data of a tile that has none yet.</summary>
    public static TileInfo Blank(int id) => new(id, null, null, [], PropertySet.Empty);

    /// <summary>Gets the tile shown at a point in time.</summary>
    public int FrameAt(double timeMilliseconds)
    {
        if (!IsAnimated)
            return Id;
        var position = (long)timeMilliseconds % AnimationLength;
        foreach (var frame in Animation)
        {
            position -= Math.Max(1, frame.DurationMilliseconds);
            if (position < 0)
                return frame.TileId;
        }

        return Animation[^1].TileId;
    }
}

/// <summary>How tile artwork is placed relative to its cell.</summary>
public enum TilePlacement
{
    /// <summary>Centered horizontally with the bottom of the artwork on the bottom of the cell, so tall art extends upward. Used by Hexy.</summary>
    BottomCenter,

    /// <summary>Centered on the cell.</summary>
    Center
}

/// <summary>A set of tiles cut from one image in a grid, or flat colors when there is no image.</summary>
/// <remarks>Changing a tileset that belongs to a map raises <see cref="TileMap.Changed"/> and bumps <see cref="TileMap.TilesetVersion"/>.</remarks>
public sealed class Tileset
{
    private readonly Dictionary<int, TileInfo> _tiles;
    private string _name;
    private TextureAsset? _texture;
    private TilesetImageFile? _imageFile;
    private int _tileWidth;
    private int _tileHeight;
    private int _margin;
    private int _spacing;
    private int _columns;
    private int _tileCount;
    private TilePlacement _placement = TilePlacement.BottomCenter;

    /// <param name="id">The id cells store; 0 lets the map assign a free one when the tileset is added.</param>
    public Tileset(int id, string name, int tileWidth, int tileHeight, TextureAsset? texture, int margin, int spacing, int columns, int tileCount,
        IReadOnlyDictionary<int, TileInfo> tiles)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tileHeight);
        Id = id;
        _name = name;
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;
        _texture = texture;
        _margin = Math.Max(0, margin);
        _spacing = Math.Max(0, spacing);
        _columns = Math.Max(0, columns);
        _tileCount = Math.Clamp(tileCount, 0, TileCell.MaxTileId + 1);
        _tiles = new Dictionary<int, TileInfo>(tiles);
        HasAnimations = ComputeHasAnimations();
    }

    /// <summary>The id stored in each <see cref="TileCell"/>; unique within the map.</summary>
    public int Id { get; internal set; }

    /// <summary>The map the tileset belongs to, or null while it is not part of one.</summary>
    public TileMap? Map { get; internal set; }

    public string Name
    {
        get => _name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_name == value)
                return;
            _name = value;
            OnChanged();
        }
    }

    /// <summary>The atlas image; null for color tilesets and for images that could not be loaded.</summary>
    public TextureAsset? Texture
    {
        get => _texture;
        set
        {
            if (ReferenceEquals(_texture, value))
                return;
            _texture = value;
            OnChanged();
        }
    }

    /// <summary>The encoded image file behind <see cref="Texture"/>, which writers store with the map; null for color tilesets.</summary>
    public TilesetImageFile? ImageFile
    {
        get => _imageFile;
        set
        {
            if (ReferenceEquals(_imageFile, value))
                return;
            _imageFile = value;
            OnChanged();
        }
    }

    public int TileWidth
    {
        get => _tileWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            SetGeometry(ref _tileWidth, value);
        }
    }

    public int TileHeight
    {
        get => _tileHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            SetGeometry(ref _tileHeight, value);
        }
    }

    /// <summary>The border in pixels around the tiles in the atlas.</summary>
    public int Margin
    {
        get => _margin;
        set => SetGeometry(ref _margin, Math.Max(0, value));
    }

    /// <summary>The gap in pixels between adjacent tiles in the atlas.</summary>
    public int Spacing
    {
        get => _spacing;
        set => SetGeometry(ref _spacing, Math.Max(0, value));
    }

    public int Columns
    {
        get => _columns;
        set => SetGeometry(ref _columns, Math.Max(0, value));
    }

    public int TileCount
    {
        get => _tileCount;
        set => SetGeometry(ref _tileCount, Math.Clamp(value, 0, TileCell.MaxTileId + 1));
    }

    public TilePlacement Placement
    {
        get => _placement;
        set
        {
            if (_placement == value)
                return;
            _placement = value;
            OnChanged();
        }
    }

    /// <summary>Tiles that have names, colors, animations, properties or collision shapes; other tiles have no entry.</summary>
    public IReadOnlyDictionary<int, TileInfo> Tiles => _tiles;

    public bool IsColorTileset => Texture is null;

    /// <summary>Whether any tile of this tileset is animated, so renderers can skip per-frame work for static tilesets.</summary>
    public bool HasAnimations { get; private set; }

    /// <summary>Data the format the tileset was read from keeps to write it back faithfully; editors leave it alone.</summary>
    public object? FormatData { get; set; }

    /// <summary>Creates a tileset cut from an image, computing its columns and tile count like Hexy does.</summary>
    public static Tileset FromImage(string name, TextureAsset texture, TilesetImageFile? imageFile, int tileWidth, int tileHeight, int margin = 0, int spacing = 0)
    {
        ArgumentNullException.ThrowIfNull(texture);
        var tileset = new Tileset(0, name, tileWidth, tileHeight, texture, margin, spacing, 0, 0, new Dictionary<int, TileInfo>()) { _imageFile = imageFile };
        tileset.FitToImage(texture.Width, texture.Height);
        return tileset;
    }

    /// <summary>Creates a color tileset with one named tile per color, like Hexy's palettes.</summary>
    public static Tileset FromColors(string name, IReadOnlyList<(string Name, Color Color)> colors, int tileWidth = 64, int tileHeight = 64)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var tiles = new Dictionary<int, TileInfo>(colors.Count);
        for (var i = 0; i < colors.Count; i++)
            tiles[i] = new TileInfo(i, colors[i].Name, colors[i].Color, [], PropertySet.Empty);
        return new Tileset(0, name, tileWidth, tileHeight, null, 0, 0, Math.Max(1, Math.Min(colors.Count, 8)), colors.Count, tiles);
    }

    public bool Contains(int tileId) => tileId >= 0 && tileId < TileCount;

    public TileInfo? Find(int tileId) => _tiles.GetValueOrDefault(tileId);

    /// <summary>Replaces the data of a tile, or removes it when <paramref name="info"/> is null or has no data, and returns the previous data.</summary>
    public TileInfo? SetTile(int tileId, TileInfo? info)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tileId);
        if (info is not null && info.Id != tileId)
            throw new ArgumentException($"The tile data is for tile {info.Id}, not {tileId}.", nameof(info));

        var previous = _tiles.GetValueOrDefault(tileId);
        if (info is { HasData: true })
        {
            if (previous == info)
                return previous;
            _tiles[tileId] = info;
        }
        else if (!_tiles.Remove(tileId))
        {
            return null;
        }

        HasAnimations = ComputeHasAnimations();
        Map?.RaiseChanged(new MapChange(MapChangeKind.TilesetChanged, Tileset: this, TileId: tileId));
        return previous;
    }

    /// <summary>Computes <see cref="Columns"/> and <see cref="TileCount"/> from the atlas size, tile size, margin and spacing.</summary>
    public void FitToImage(int imageWidth, int imageHeight)
    {
        var columns = Math.Max(0, (imageWidth - 2 * Margin + Spacing) / (TileWidth + Spacing));
        var rows = Math.Max(0, (imageHeight - 2 * Margin + Spacing) / (TileHeight + Spacing));
        Columns = columns;
        TileCount = columns * rows;
    }

    /// <summary>The tile's rectangle in the atlas, in pixels.</summary>
    public Rect2 SourceRect(int tileId)
    {
        var columns = Math.Max(1, Columns);
        return new Rect2(Margin + tileId % columns * (TileWidth + Spacing), Margin + tileId / columns * (TileHeight + Spacing), TileWidth, TileHeight);
    }

    public override string ToString() => Name;

    private void SetGeometry(ref int field, int value)
    {
        if (field == value)
            return;
        field = value;
        OnChanged();
    }

    private void OnChanged() => Map?.RaiseChanged(new MapChange(MapChangeKind.TilesetChanged, Tileset: this));

    private bool ComputeHasAnimations()
    {
        foreach (var tile in _tiles.Values)
        {
            if (tile.IsAnimated)
                return true;
        }

        return false;
    }
}
