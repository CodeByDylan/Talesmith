using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Runtime.Maps;
using Color = Avalonia.Media.Color;
using PixelRect = Avalonia.PixelRect;

namespace Talesmith.Editor.TileMaps.Rendering;

/// <summary>Tile artwork for Avalonia: tileset atlases as bitmaps, single tiles as images and color tiles as brushes, drawn the way the game places
/// them.</summary>
public static class TileArt
{
    private static readonly ConditionalWeakTable<TextureAsset, Bitmap> Atlases = [];
    private static readonly Dictionary<uint, IImmutableSolidColorBrush> ColorBrushes = [];

    /// <summary>The tileset's atlas as a bitmap, or null for color tilesets.</summary>
    public static Bitmap? Atlas(Tileset tileset)
    {
        ArgumentNullException.ThrowIfNull(tileset);
        return tileset.Texture is { } texture ? Atlases.GetValue(texture, CreateBitmap) : null;
    }

    /// <summary>One tile's artwork, or null for color tiles and tiles outside the atlas.</summary>
    public static IImage? Image(Tileset tileset, int tileId)
    {
        if (Atlas(tileset) is not { } atlas || !tileset.Contains(tileId))
            return null;
        var source = SourceRect(tileset, tileId, atlas);
        return source.Width <= 0 || source.Height <= 0 ? null : new CroppedBitmap(atlas, source);
    }

    /// <summary>The tile's rectangle in the atlas, clipped to the image.</summary>
    public static PixelRect SourceRect(Tileset tileset, int tileId, Bitmap atlas)
    {
        var rect = tileset.SourceRect(tileId);
        return new PixelRect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height).Intersect(new PixelRect(atlas.PixelSize));
    }

    /// <summary>The flat color of a tile: its own color, or the fallback color of color tilesets; transparent for image tiles without one.</summary>
    public static IImmutableSolidColorBrush ColorBrush(Tileset tileset, int tileId)
    {
        ArgumentNullException.ThrowIfNull(tileset);
        if (tileset.Find(tileId)?.Color is { } color)
            return Brush(ToAvalonia(color));
        return tileset.IsColorTileset ? Brush(ToAvalonia(TileGeometry.FallbackColor(tileId))) : (IImmutableSolidColorBrush)Brushes.Transparent.ToImmutable();
    }

    public static Color ToAvalonia(Mathematics.Color color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static Mathematics.Color FromAvalonia(Color color) => new(color.R, color.G, color.B, color.A);

    /// <summary>Draws a placed tile in its cell as the game does: anchored by the tileset's placement, flipped and rotated around the cell center.</summary>
    /// <param name="worldToScreen">The transform from world to the drawing context's coordinates.</param>
    /// <param name="cellOutline">The cell's outline in drawing coordinates, used for color tiles.</param>
    public static void DrawCellTile(DrawingContext drawing, Matrix worldToScreen, TileMap map, Vector2 cellCenter, TileCell tile, Geometry? cellOutline,
        double timeMilliseconds = 0)
    {
        if (tile.IsEmpty || map.FindTileset(tile.TilesetId) is not { } tileset || !tileset.Contains(tile.TileId))
            return;
        if (Atlas(tileset) is { } atlas)
        {
            var frame = tileset.Find(tile.TileId)?.FrameAt(timeMilliseconds) ?? tile.TileId;
            var quad = ToMatrix(TileGeometry.CellTile(map, tileset, cellCenter, tile));
            DrawQuad(drawing, atlas, SourceRect(tileset, frame, atlas), quad * worldToScreen);
            return;
        }

        if (cellOutline is not null)
            drawing.DrawGeometry(ColorBrush(tileset, tile.TileId), null, cellOutline);
    }

    /// <summary>Draws a tile image centered on a point, as tile objects are drawn.</summary>
    public static void DrawCentered(DrawingContext drawing, Matrix worldToScreen, TileMap map, Vector2 center, TileCell tile)
    {
        if (tile.IsEmpty || map.FindTileset(tile.TilesetId) is not { } tileset || !tileset.Contains(tile.TileId))
            return;
        var size = new Vector2(tileset.TileWidth, tileset.TileHeight);
        var quad = ToMatrix(TileGeometry.Centered(size, center, tile, map.RotationStepDegrees));
        if (Atlas(tileset) is { } atlas)
        {
            DrawQuad(drawing, atlas, SourceRect(tileset, tile.TileId, atlas), quad * worldToScreen);
            return;
        }

        using (drawing.PushTransform(quad * worldToScreen))
            drawing.DrawRectangle(ColorBrush(tileset, tile.TileId), null, new Rect(0, 0, 1, 1), 0.08, 0.08);
    }

    public static Matrix ToMatrix(Matrix3x2 m) => new(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

    private static void DrawQuad(DrawingContext drawing, Bitmap atlas, PixelRect source, Matrix transform)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return;
        using (drawing.PushTransform(transform))
            drawing.DrawImage(atlas, new Rect(source.X, source.Y, source.Width, source.Height), new Rect(0, 0, 1, 1));
    }

    private static IImmutableSolidColorBrush Brush(Color color)
    {
        lock (ColorBrushes)
        {
            if (!ColorBrushes.TryGetValue(color.ToUInt32(), out var brush))
                ColorBrushes[color.ToUInt32()] = brush = new ImmutableSolidColorBrush(color);
            return brush;
        }
    }

    private static Bitmap CreateBitmap(TextureAsset texture)
    {
        var image = texture.Image;
        var handle = GCHandle.Alloc(image.Pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Premul, handle.AddrOfPinnedObject(), new PixelSize(image.Width, image.Height), new global::Avalonia.Vector(96, 96),
                image.Stride);
        }
        finally
        {
            handle.Free();
        }
    }
}
