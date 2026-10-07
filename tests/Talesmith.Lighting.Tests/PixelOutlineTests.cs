using System.Numerics;

namespace Talesmith.Lighting.Tests;

public sealed class PixelOutlineTests
{
    [Fact]
    public void ARectangleBecomesItsFourCornersClockwise()
    {
        var loops = PixelOutline.Trace(Mask(6, 4, (x, y) => x is >= 1 and <= 4 && y is >= 1 and <= 2), 6, 4);

        var loop = Assert.Single(loops);
        Assert.Equal([new(1, 1), new(5, 1), new(5, 3), new(1, 3)], loop);
        Assert.True(SignedArea(loop) > 0);
    }

    [Fact]
    public void HolesRunCounterClockwise()
    {
        var loops = PixelOutline.Trace(Mask(5, 5, (x, y) => x != 2 || y != 2), 5, 5);

        Assert.Equal(2, loops.Count);
        Assert.Contains(loops, l => SignedArea(l) == 2 * 25);
        Assert.Contains(loops, l => SignedArea(l) == -2 * 1);
    }

    [Fact]
    public void PixelsTouchingAtACornerStayApart()
    {
        var loops = PixelOutline.Trace(Mask(2, 2, (x, y) => x == y), 2, 2);

        Assert.Equal(2, loops.Count);
        Assert.All(loops, l => Assert.Equal(4, l.Length));
    }

    [Fact]
    public void StaircasesAreStraightened()
    {
        // A right triangle, 16 pixels on each side: its diagonal is a staircase of 16 steps.
        var loops = PixelOutline.Trace(Mask(16, 16, (x, y) => x <= y), 16, 16);

        var loop = Assert.Single(loops);
        Assert.InRange(loop.Length, 3, 6);
        Assert.Equal(16 * 17 / 2.0, SignedArea(loop) / 2, 16.0);
    }

    [Fact]
    public void StraightRunsKeepTheirPlaceBetweenStraightenedCorners()
    {
        // A rectangle with three pixel steps cut off each corner: its sides stay on the pixel grid, its corners become one slanted edge.
        var loops = PixelOutline.Trace(Mask(20, 12, (x, y) => Math.Min(x, 19 - x) + Math.Min(y, 11 - y) >= 3), 20, 12);

        var loop = Assert.Single(loops);
        Vector2[] octagon = [new(3, 0), new(17, 0), new(20, 3), new(20, 9), new(17, 12), new(3, 12), new(0, 9), new(0, 3)];
        Assert.Equal(octagon.Length, loop.Length);
        Assert.All(octagon, corner => Assert.Contains(corner, loop));
        Assert.True(SignedArea(loop) > 0);
    }

    [Fact]
    public void AnEmptyMaskHasNoOutline() => Assert.Empty(PixelOutline.Trace(new bool[12], 4, 3));

    [Fact]
    public void TheMaskMustMatchItsSize() => Assert.Throws<ArgumentException>(() => PixelOutline.Trace(new bool[10], 4, 3));

    private static bool[] Mask(int width, int height, Func<int, int, bool> set)
    {
        var mask = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
                mask[y * width + x] = set(x, y);
        }

        return mask;
    }

    /// <summary>Twice the signed area; positive when the points run clockwise on screen.</summary>
    private static float SignedArea(Vector2[] points)
    {
        var area = 0f;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area;
    }
}
