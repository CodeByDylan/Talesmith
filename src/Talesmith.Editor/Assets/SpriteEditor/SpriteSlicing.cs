using Talesmith.Assets.Textures;
using Talesmith.Imaging;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>How <see cref="SpriteSlicing.Grid"/> cuts an image into cells.</summary>
/// <param name="CellWidth">The width of a cell, used when <see cref="Columns"/> is 0.</param>
/// <param name="CellHeight">The height of a cell, used when <see cref="Rows"/> is 0.</param>
/// <param name="Columns">A number of columns to fit into the image instead of a cell width; 0 uses <see cref="CellWidth"/>.</param>
/// <param name="Rows">A number of rows to fit into the image instead of a cell height; 0 uses <see cref="CellHeight"/>.</param>
public sealed record GridSliceOptions(int CellWidth, int CellHeight, int Columns = 0, int Rows = 0)
{
    public int OffsetX { get; init; }

    public int OffsetY { get; init; }

    /// <summary>Pixels between columns.</summary>
    public int PaddingX { get; init; }

    /// <summary>Pixels between rows.</summary>
    public int PaddingY { get; init; }

    /// <summary>Leaves out cells whose pixels are all transparent.</summary>
    public bool SkipEmpty { get; init; } = true;
}

/// <summary>Finds sprite rectangles in images: by a grid, or by islands of opaque pixels.</summary>
public static class SpriteSlicing
{
    /// <summary>The cells of a grid, left to right and top to bottom; cells that do not fit in the image are left out.</summary>
    public static IReadOnlyList<PixelRect> Grid(ImageData image, GridSliceOptions options)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(options);
        var paddingX = Math.Max(0, options.PaddingX);
        var paddingY = Math.Max(0, options.PaddingY);
        var offsetX = Math.Max(0, options.OffsetX);
        var offsetY = Math.Max(0, options.OffsetY);
        var width = options.Columns > 0 ? (image.Width - offsetX - paddingX * (options.Columns - 1)) / options.Columns : options.CellWidth;
        var height = options.Rows > 0 ? (image.Height - offsetY - paddingY * (options.Rows - 1)) / options.Rows : options.CellHeight;
        if (width <= 0 || height <= 0)
            return [];
        var cells = new List<PixelRect>();
        for (var y = offsetY; y + height <= image.Height; y += height + paddingY)
        {
            for (var x = offsetX; x + width <= image.Width; x += width + paddingX)
            {
                var cell = new PixelRect(x, y, width, height);
                if (!options.SkipEmpty || !IsTransparent(image, cell))
                    cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>The bounding rectangles of groups of touching opaque pixels, top to bottom in rows, then left to right.</summary>
    /// <param name="alphaThreshold">Pixels with an alpha at or below this count as transparent.</param>
    /// <param name="minimumSize">Islands smaller than this in both directions, such as stray pixels, are left out.</param>
    /// <param name="mergeDistance">Islands whose rectangles come within this many pixels of each other become one, such as a sword apart from its hero.</param>
    public static IReadOnlyList<PixelRect> AlphaIslands(ImageData image, byte alphaThreshold = 0, int minimumSize = 2, int mergeDistance = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        var width = image.Width;
        var height = image.Height;
        var pixels = image.Pixels;
        var visited = new bool[width * height];
        var stack = new Stack<int>();
        var islands = new List<PixelRect>();
        for (var start = 0; start < visited.Length; start++)
        {
            if (visited[start] || pixels[start * 4 + 3] <= alphaThreshold)
                continue;
            int left = start % width, right = left, top = start / width, bottom = top;
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var index = stack.Pop();
                var x = index % width;
                var y = index / width;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= height)
                        continue;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var nx = x + dx;
                        if (nx < 0 || nx >= width)
                            continue;
                        var neighbor = ny * width + nx;
                        if (!visited[neighbor] && pixels[neighbor * 4 + 3] > alphaThreshold)
                        {
                            visited[neighbor] = true;
                            stack.Push(neighbor);
                        }
                    }
                }
            }

            islands.Add(new PixelRect(left, top, right - left + 1, bottom - top + 1));
        }

        if (mergeDistance > 0)
            islands = Merge(islands, mergeDistance);
        islands.RemoveAll(r => r.Width < minimumSize && r.Height < minimumSize);
        return SortInRows(islands);
    }

    /// <summary>Whether every pixel of a region is fully transparent.</summary>
    public static bool IsTransparent(ImageData image, PixelRect rect)
    {
        ArgumentNullException.ThrowIfNull(image);
        for (var y = Math.Max(0, rect.Y); y < Math.Min(image.Height, rect.Bottom); y++)
        {
            var row = y * image.Width * 4;
            for (var x = Math.Max(0, rect.X); x < Math.Min(image.Width, rect.Right); x++)
            {
                if (image.Pixels[row + x * 4 + 3] != 0)
                    return false;
            }
        }

        return true;
    }

    private static List<PixelRect> Merge(List<PixelRect> rects, int distance)
    {
        var merged = true;
        while (merged)
        {
            merged = false;
            for (var i = 0; i < rects.Count && !merged; i++)
            {
                for (var j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i];
                    var b = rects[j];
                    if (a.X - distance < b.Right && b.X - distance < a.Right && a.Y - distance < b.Bottom && b.Y - distance < a.Bottom)
                    {
                        var left = Math.Min(a.X, b.X);
                        var top = Math.Min(a.Y, b.Y);
                        rects[i] = new PixelRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
                        rects.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            }
        }

        return rects;
    }

    /// <summary>Orders rectangles in reading order: rectangles whose vertical spans overlap form a row, rows go top to bottom.</summary>
    private static List<PixelRect> SortInRows(List<PixelRect> rects)
    {
        var rows = new List<List<PixelRect>>();
        foreach (var rect in rects.OrderBy(r => r.Y))
        {
            var row = rows.FirstOrDefault(r => r.Any(o => rect.Y < o.Bottom && o.Y < rect.Bottom));
            if (row is null)
                rows.Add([rect]);
            else
                row.Add(rect);
        }

        return [.. rows.SelectMany(r => r.OrderBy(o => o.X))];
    }
}
