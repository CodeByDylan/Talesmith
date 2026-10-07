using System.Numerics;
using System.Runtime.CompilerServices;
using Talesmith.Assets.Maps;
using Talesmith.Imaging;
using Talesmith.Runtime.Maps;

namespace Talesmith.Lighting;

/// <summary>The shapes tiles block light with: the opaque pixels of their artwork inside their cell.</summary>
/// <remarks>
/// A tile whose artwork covers its whole cell blocks the cell itself, so neighboring solid cells share exact edges. Other tiles block the
/// outline of their opaque pixels (alpha of at least a half), placed, flipped and turned like the artwork and cut to the cell, so empty
/// parts of a tile, such as the space under a plank, cast no shadow. Outlines within <see cref="SealDistance"/> pixels of the cell's
/// edge are pulled onto it, so blocks drawn with a soft or inset rim seal against their neighbors and light does not leak between
/// them. Outlines are traced once per tile, rotation and flip, and again after the map's tilesets change.
/// </remarks>
internal sealed class TileArtShapes
{
    private const byte OpaqueAlpha = 128;

    /// <summary>How close to the cell's edge, in pixels of the artwork, an outline is pulled onto it.</summary>
    private const float SealDistance = 2;

    private readonly ConditionalWeakTable<Tileset, Dictionary<(int Tile, int Rotation, bool FlipX), Entry>> _shapes = new();

    /// <summary>The loops, relative to the cell's center and running clockwise around solid parts, that a tile blocks light with.</summary>
    /// <param name="corners">The cell's corners relative to its center, clockwise.</param>
    /// <returns>The loops, or null when the tile blocks its whole cell, as color tiles and tiles whose artwork covers the cell do.</returns>
    public IReadOnlyList<Vector2[]>? For(TileMap map, TileCell cell, ReadOnlySpan<Vector2> corners)
    {
        if (map.FindTileset(cell.TilesetId) is not { Texture.Image: { } image } tileset)
            return null;
        var shapes = _shapes.GetValue(tileset, _ => []);
        var key = (cell.TileId, cell.Rotation, cell.FlipX);
        if (shapes.TryGetValue(key, out var entry) && entry.Version == map.TilesetVersion)
            return entry.Loops;

        var loops = Trace(map, tileset, image, cell, corners);
        shapes[key] = new Entry(map.TilesetVersion, loops);
        return loops;
    }

    private static Vector2[][]? Trace(TileMap map, Tileset tileset, ImageData image, TileCell cell, ReadOnlySpan<Vector2> corners)
    {
        var source = tileset.SourceRect(cell.TileId);
        var (left, top, width, height) = ((int)source.X, (int)source.Y, (int)source.Width, (int)source.Height);
        if (width <= 0 || height <= 0)
            return null;

        var toCell = TileGeometry.CellTile(map, tileset, Vector2.Zero, cell);
        var mask = new bool[width * height];
        var gaps = false;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!Contains(corners, Vector2.Transform(new Vector2((x + 0.5f) / width, (y + 0.5f) / height), toCell)))
                    continue;
                var opaque = Alpha(image, left + x, top + y) >= OpaqueAlpha;
                mask[y * width + x] = opaque;
                gaps |= !opaque;
            }
        }

        if (!Matrix3x2.Invert(toCell, out var toArt) || (!gaps && Covers(toArt, corners)))
            return null;

        // The cell's corners in pixels of the artwork, to seal outlines against.
        var size = new Vector2(width, height);
        Span<Vector2> rim = stackalloc Vector2[corners.Length];
        for (var i = 0; i < rim.Length; i++)
            rim[i] = Vector2.Transform(corners[i], toArt) * size;

        var traced = PixelOutline.Trace(mask, width, height);
        var mirrored = toCell.GetDeterminant() < 0;
        var loops = new Vector2[traced.Count][];
        for (var i = 0; i < loops.Length; i++)
        {
            var loop = traced[i];
            loops[i] = new Vector2[loop.Length];
            for (var j = 0; j < loop.Length; j++)
                loops[i][mirrored ? loop.Length - 1 - j : j] = Vector2.Transform(Seal(loop[j], rim) / size, toCell);
        }

        return loops;
    }

    /// <summary>A point pulled onto the cell's rim when it lies within <see cref="SealDistance"/> of it, or onto a corner when it lies that
    /// close to the two sides meeting there.</summary>
    /// <param name="rim">The cell's corners, in pixels of the artwork.</param>
    private static Vector2 Seal(Vector2 point, ReadOnlySpan<Vector2> rim)
    {
        var near = -1;
        for (var i = 0; i < rim.Length; i++)
        {
            if (DistanceToSegment(point, rim[i], rim[(i + 1) % rim.Length]) > SealDistance)
                continue;
            if (near >= 0)
                return rim[(near + 1) % rim.Length == i ? i : near];
            near = i;
        }

        if (near < 0)
            return point;
        var a = rim[near];
        var side = rim[(near + 1) % rim.Length] - a;
        return a + side * Math.Clamp(Vector2.Dot(point - a, side) / side.LengthSquared(), 0, 1);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        var segment = b - a;
        var lengthSquared = segment.LengthSquared();
        var t = lengthSquared > 0 ? Math.Clamp(Vector2.Dot(point - a, segment) / lengthSquared, 0, 1) : 0;
        return Vector2.Distance(point, a + segment * t);
    }

    private static byte Alpha(ImageData image, int x, int y) =>
        x < 0 || y < 0 || x >= image.Width || y >= image.Height ? (byte)0 : image.Pixels[y * image.Stride + x * 4 + 3];

    /// <summary>Whether a point is inside the convex cell whose corners run clockwise.</summary>
    private static bool Contains(ReadOnlySpan<Vector2> corners, Vector2 point)
    {
        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            if ((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X) < 0)
                return false;
        }

        return true;
    }

    /// <summary>Whether the artwork's quad contains every corner of the cell.</summary>
    private static bool Covers(in Matrix3x2 toArt, ReadOnlySpan<Vector2> corners)
    {
        const float Tolerance = 1e-3f;
        foreach (var corner in corners)
        {
            var uv = Vector2.Transform(corner, toArt);
            if (uv.X < -Tolerance || uv.Y < -Tolerance || uv.X > 1 + Tolerance || uv.Y > 1 + Tolerance)
                return false;
        }

        return true;
    }

    private sealed record Entry(long Version, Vector2[][]? Loops);
}
