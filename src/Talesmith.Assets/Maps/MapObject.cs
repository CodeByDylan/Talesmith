using System.Numerics;

namespace Talesmith.Assets.Maps;

/// <summary>The geometry of a map object.</summary>
public enum MapObjectShape
{
    /// <summary>A single position, such as a spawn point.</summary>
    Point,

    /// <summary>A closed area, such as a trigger zone.</summary>
    Polygon,

    /// <summary>A tile image placed as a free object, such as a building or tree.</summary>
    Tile
}

/// <summary>An object placed on an <see cref="ObjectLayer"/>, in world units relative to the center of the map's cell (0, 0).</summary>
/// <remarks>A map spawned at another origin moves its object entities by that origin; the object itself keeps map coordinates.</remarks>
/// <param name="Position">The object's position: the point itself, the polygon's reference point or the center of a tile image.</param>
/// <param name="Polygon">For polygons, the vertices relative to <see cref="Position"/>; otherwise empty.</param>
/// <param name="Tile">For tile objects, the tile to draw centered on <see cref="Position"/>.</param>
/// <param name="Cell">The grid cell the object is anchored to.</param>
public sealed record MapObject(
    int Id,
    string Name,
    string Type,
    MapObjectShape Shape,
    Vector2 Position,
    Grids.GridCoord Cell,
    IReadOnlyList<Vector2> Polygon,
    TileCell Tile,
    PropertySet Properties)
{
    /// <summary>Data the format the object was read from keeps to write it back faithfully; editors leave it alone.</summary>
    public object? FormatData { get; init; }

    /// <summary>Gets a copy placed at another position, anchored to the cell containing it.</summary>
    public MapObject MoveTo(Vector2 position, Grids.IGridLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return this with { Position = position, Cell = layout.WorldToCell(position) };
    }

    /// <summary>Whether a point, in the same map coordinates as <see cref="Position"/>, lies inside the polygon; false for other shapes.</summary>
    public bool Contains(Vector2 world)
    {
        if (Shape != MapObjectShape.Polygon || Polygon.Count < 3)
            return false;
        var point = world - Position;
        var inside = false;
        for (int i = 0, j = Polygon.Count - 1; i < Polygon.Count; j = i++)
        {
            var a = Polygon[i];
            var b = Polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}
