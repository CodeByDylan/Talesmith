using Talesmith.Assets.Textures;

namespace Talesmith.Assets.Atlases;

/// <summary>Places rectangles in a fixed-size bin with the MaxRects algorithm, choosing the free spot with the best short side fit.</summary>
/// <remarks>Rectangles are never rotated. Packing is deterministic: the same inserts in the same order give the same placements.</remarks>
internal sealed class MaxRectsPacker
{
    private readonly List<PixelRect> _free = [];
    private readonly List<PixelRect> _used = [];

    public MaxRectsPacker(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        _free.Add(new PixelRect(0, 0, width, height));
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The rectangles placed so far.</summary>
    public IReadOnlyList<PixelRect> Used => _used;

    /// <summary>Places a rectangle, or returns false when no free spot is large enough.</summary>
    public bool TryInsert(int width, int height, out PixelRect placed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        placed = default;
        var bestShort = int.MaxValue;
        var bestLong = int.MaxValue;
        foreach (var free in _free)
        {
            if (free.Width < width || free.Height < height)
                continue;

            var leftoverX = free.Width - width;
            var leftoverY = free.Height - height;
            var shortSide = Math.Min(leftoverX, leftoverY);
            var longSide = Math.Max(leftoverX, leftoverY);
            if (shortSide < bestShort || (shortSide == bestShort && longSide < bestLong))
            {
                placed = new PixelRect(free.X, free.Y, width, height);
                bestShort = shortSide;
                bestLong = longSide;
            }
        }

        if (bestShort == int.MaxValue)
            return false;

        SplitFreeRectangles(placed);
        PruneFreeRectangles();
        _used.Add(placed);
        return true;
    }

    private void SplitFreeRectangles(PixelRect used)
    {
        for (var i = _free.Count - 1; i >= 0; i--)
        {
            var free = _free[i];
            if (!free.Intersects(used))
                continue;

            _free.RemoveAt(i);
            if (used.X > free.X)
                _free.Add(free with { Width = used.X - free.X });
            if (used.Right < free.Right)
                _free.Add(free with { X = used.Right, Width = free.Right - used.Right });
            if (used.Y > free.Y)
                _free.Add(free with { Height = used.Y - free.Y });
            if (used.Bottom < free.Bottom)
                _free.Add(free with { Y = used.Bottom, Height = free.Bottom - used.Bottom });
        }
    }

    private void PruneFreeRectangles()
    {
        for (var i = 0; i < _free.Count; i++)
        {
            for (var j = i + 1; j < _free.Count; j++)
            {
                if (_free[j].Contains(_free[i]))
                {
                    _free.RemoveAt(i--);
                    break;
                }

                if (_free[i].Contains(_free[j]))
                    _free.RemoveAt(j--);
            }
        }
    }
}

/// <summary>Options for <see cref="AtlasLayout.Pack"/>.</summary>
/// <param name="Padding">Empty pixels between rectangles and around the atlas border.</param>
/// <param name="MaxSize">The largest width or height the atlas may have.</param>
/// <param name="PowerOfTwo">Whether the atlas width and height must be powers of two.</param>
public readonly record struct AtlasPackOptions(int Padding = 2, int MaxSize = 4096, bool PowerOfTwo = false);

/// <summary>Where each rectangle went in a packed atlas.</summary>
public sealed record AtlasLayout(int Width, int Height, IReadOnlyList<PixelRect> Placements)
{
    /// <summary>Packs rectangles into the smallest atlas found, trying sizes from the total area up to the maximum size.</summary>
    /// <param name="sizes">The width and height of each rectangle; placements are returned in the same order.</param>
    /// <exception cref="AssetException">The rectangles do not fit in an atlas of the maximum size.</exception>
    public static AtlasLayout Pack(IReadOnlyList<(int Width, int Height)> sizes, AtlasPackOptions options)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentOutOfRangeException.ThrowIfNegative(options.Padding);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSize);
        var padding = options.Padding;
        if (sizes.Count == 0)
            return new AtlasLayout(1, 1, []);

        var order = Enumerable.Range(0, sizes.Count)
            .OrderByDescending(i => Math.Max(sizes[i].Width, sizes[i].Height))
            .ThenByDescending(i => sizes[i].Width * sizes[i].Height)
            .ThenBy(i => i)
            .ToArray();
        var area = sizes.Sum(size => (long)(size.Width + padding) * (size.Height + padding));
        var minWidth = sizes.Max(size => size.Width) + padding * 2;
        var minHeight = sizes.Max(size => size.Height) + padding * 2;
        if (minWidth > options.MaxSize || minHeight > options.MaxSize)
            throw new AssetException($"A {minWidth - padding * 2}×{minHeight - padding * 2} image does not fit in an atlas of at most {options.MaxSize} pixels.");

        var steps = Steps(Math.Min(minWidth, minHeight), options).ToArray();
        AtlasLayout? best = null;
        foreach (var width in steps.Where(step => step >= minWidth))
        {
            var heights = steps.Where(step => step >= minHeight && (long)width * step >= area).ToArray();
            if (heights.Length == 0 || (best is not null && (long)width * heights[0] >= best.Area))
                continue;

            // Whether a height fits is close enough to monotonic to search for the smallest one.
            if (TryPack(sizes, order, width, heights[^1], padding) is not { } placements)
                continue;
            int low = 0, high = heights.Length - 1;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (TryPack(sizes, order, width, heights[middle], padding) is { } fitted)
                {
                    high = middle;
                    placements = fitted;
                }
                else
                {
                    low = middle + 1;
                }
            }

            var layout = Shrink(width, heights[low], placements, padding, options.PowerOfTwo);
            if (best is null || layout.Area < best.Area || (layout.Area == best.Area && Math.Abs(layout.Width - layout.Height) < Math.Abs(best.Width - best.Height)))
                best = layout;
        }

        return best ?? throw new AssetException($"{sizes.Count} images do not fit in an atlas of at most {options.MaxSize}×{options.MaxSize} pixels.");
    }

    private long Area => (long)Width * Height;

    private static IEnumerable<int> Steps(int minimum, AtlasPackOptions options)
    {
        if (options.PowerOfTwo)
        {
            for (var size = 1; size <= options.MaxSize && size > 0; size <<= 1)
            {
                if (size >= minimum)
                    yield return size;
            }

            yield break;
        }

        var increment = Math.Max(16, options.MaxSize / 64);
        for (var size = minimum; size < options.MaxSize; size += increment)
            yield return size;
        yield return options.MaxSize;
    }

    private static PixelRect[]? TryPack(IReadOnlyList<(int Width, int Height)> sizes, int[] order, int width, int height, int padding)
    {
        var packer = new MaxRectsPacker(width - padding, height - padding);
        var placements = new PixelRect[sizes.Count];
        foreach (var index in order)
        {
            var (w, h) = sizes[index];
            if (!packer.TryInsert(w + padding, h + padding, out var spot))
                return null;
            placements[index] = new PixelRect(spot.X + padding, spot.Y + padding, w, h);
        }

        return placements;
    }

    private static AtlasLayout Shrink(int width, int height, PixelRect[] placements, int padding, bool powerOfTwo)
    {
        if (powerOfTwo)
            return new AtlasLayout(width, height, placements);

        var usedWidth = placements.Max(rect => rect.Right) + padding;
        var usedHeight = placements.Max(rect => rect.Bottom) + padding;
        return new AtlasLayout(Math.Min(width, usedWidth), Math.Min(height, usedHeight), placements);
    }
}
