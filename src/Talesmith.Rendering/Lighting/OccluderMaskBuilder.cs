using System.Numerics;

namespace Talesmith.Rendering.Lighting;

/// <summary>Rasterizes the occluders that do not shadow themselves into a mask the size of the light map, telling light shaders where
/// shadows do not fall.</summary>
/// <remarks>
/// <para>Shadows darken what lies behind occluders. An occluder that does not shadow itself stays lit inside, even where another one's
/// shadow falls on it, so a stack of crates or a row of platforms is lit evenly by the lights around it while their shadows fall on the
/// background. Each byte holds how much of its pixel those occluders cover, from 0 to 255, so their edges are smooth; overlapping
/// occluders count once and holes stay open.</para>
/// <para>Coverage is added up edge by edge, so outlines must be closed. Building is backend-independent and runs on the render thread;
/// buffers are reused between frames.</para>
/// </remarks>
public sealed class OccluderMaskBuilder
{
    private float[] _accumulation = [];
    private byte[] _data = [];
    private int _stride;
    private int _firstRow;
    private int _endRow;

    /// <summary>The pixels per row of the last <see cref="Build"/>.</summary>
    public int Width { get; private set; }

    /// <summary>The rows of the last <see cref="Build"/>.</summary>
    public int Height { get; private set; }

    /// <summary>The coverage of every pixel, <see cref="Width"/> per row, top row first.</summary>
    public ReadOnlySpan<byte> Data => _data.AsSpan(0, Width * Height);

    /// <summary>Builds the mask of a light map <paramref name="width"/> by <paramref name="height"/> pixels big.</summary>
    /// <param name="worldToMap">Maps world positions to light map pixels, where pixel (x, y) spans x to x + 1 and y to y + 1.</param>
    /// <returns>Whether occluders cover any pixel.</returns>
    public bool Build(LightingFrame lighting, in Matrix3x2 worldToMap, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Prepare(width, height);

        var edges = lighting.Edges;
        foreach (ref readonly var group in lighting.Occluders)
        {
            // Closed outlines entirely beside the map add nothing to it.
            var bounds = group.Bounds.Transform(worldToMap);
            if (group.SelfShadows || bounds.Right <= 0 || bounds.Left >= width || bounds.Bottom <= 0 || bounds.Top >= height)
                continue;
            foreach (ref readonly var edge in edges.Slice(group.FirstEdge, group.EdgeCount))
                AddEdge(Vector2.Transform(edge.Start, worldToMap), Vector2.Transform(edge.End, worldToMap));
        }

        return Resolve();
    }

    private void Prepare(int width, int height)
    {
        Width = width;
        Height = height;
        _firstRow = height;
        _endRow = 0;
        // Two spare cells per row take what edges add right of the map.
        _stride = width + 2;
        if (_accumulation.Length < _stride * height)
            _accumulation = new float[_stride * height];
        if (_data.Length < width * height)
            _data = new byte[width * height];
        else
            _data.AsSpan(0, width * height).Clear();
    }

    /// <summary>Adds how an edge changes the coverage of the cells right of it, row by row.</summary>
    /// <remarks>The signed-area accumulation of font-rs: summing a row from the left then gives each pixel's winding, antialiased.</remarks>
    private void AddEdge(Vector2 from, Vector2 to)
    {
        if (from.Y == to.Y || !float.IsFinite(from.X + from.Y + to.X + to.Y))
            return;
        var direction = 1f;
        if (from.Y > to.Y)
        {
            (from, to) = (to, from);
            direction = -1;
        }

        if (to.Y <= 0 || from.Y >= Height)
            return;
        var dxdy = (to.X - from.X) / (to.Y - from.Y);
        var first = Math.Max(0, (int)MathF.Floor(from.Y));
        var end = Math.Min(Height, (int)MathF.Ceiling(to.Y));
        var x = from.X + (MathF.Max(from.Y, first) - from.Y) * dxdy;
        _firstRow = Math.Min(_firstRow, first);
        _endRow = Math.Max(_endRow, end);
        for (var y = first; y < end; y++)
        {
            var dy = MathF.Min(y + 1, to.Y) - MathF.Max(y, from.Y);
            var next = x + dxdy * dy;
            // Left of the map, an edge covers the whole row after it; right of it, nothing that is drawn.
            AddRow(y * _stride, Math.Clamp(MathF.Min(x, next), 0, Width), Math.Clamp(MathF.Max(x, next), 0, Width), dy * direction);
            x = next;
        }
    }

    /// <summary>Spreads the coverage <paramref name="change"/> of an edge crossing one row from <paramref name="x0"/> to <paramref name="x1"/>
    /// over the cells it passes, so the cells right of it change by all of it.</summary>
    private void AddRow(int row, float x0, float x1, float change)
    {
        var cells = _accumulation;
        var x0Floor = MathF.Floor(x0);
        var first = row + (int)x0Floor;
        var x1Ceiling = MathF.Ceiling(x1);
        var count = (int)x1Ceiling - (int)x0Floor;
        if (count <= 1)
        {
            var middle = (x0 + x1) / 2 - x0Floor;
            cells[first] += change * (1 - middle);
            cells[first + 1] += change * middle;
            return;
        }

        var slope = 1 / (x1 - x0);
        var x0Fraction = x0 - x0Floor;
        var firstArea = slope * (1 - x0Fraction) * (1 - x0Fraction) / 2;
        var x1Fraction = x1 - x1Ceiling + 1;
        var lastArea = slope * x1Fraction * x1Fraction / 2;
        cells[first] += change * firstArea;
        if (count == 2)
        {
            cells[first + 1] += change * (1 - firstArea - lastArea);
        }
        else
        {
            var secondArea = slope * (1.5f - x0Fraction);
            cells[first + 1] += change * (secondArea - firstArea);
            for (var i = 2; i < count - 1; i++)
                cells[first + i] += change * slope;
            var beforeLast = secondArea + (count - 3) * slope;
            cells[first + count - 1] += change * (1 - beforeLast - lastArea);
        }

        cells[first + count] += change * lastArea;
    }

    /// <summary>Sums the touched rows into coverage and clears them for the next build; returns whether any pixel is covered.</summary>
    private bool Resolve()
    {
        var any = false;
        for (var y = _firstRow; y < _endRow; y++)
        {
            var cells = _accumulation.AsSpan(y * _stride, _stride);
            var pixels = _data.AsSpan(y * Width, Width);
            var winding = 0f;
            for (var x = 0; x < pixels.Length; x++)
            {
                winding += cells[x];
                var coverage = (byte)(MathF.Min(MathF.Abs(winding), 1) * 255 + 0.5f);
                pixels[x] = coverage;
                any |= coverage != 0;
            }

            cells.Clear();
        }

        return any;
    }
}
