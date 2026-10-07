using System.Numerics;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Rendering.Tests;

public sealed class OccluderMaskBuilderTests
{
    [Fact]
    public void OccludersCoverTheirPixelsWithSmoothEdges()
    {
        var builder = Build(lighting => lighting.AddOccluder(Rectangle(2.5f, 1, 6, 3), 1, false));

        Assert.Equal([0, 0, 128, 255, 255, 255, 0, 0], Row(builder, 1));
        Assert.Equal(Row(builder, 1), Row(builder, 2));
        Assert.All(Row(builder, 0), coverage => Assert.Equal(0, coverage));
        Assert.All(Row(builder, 3), coverage => Assert.Equal(0, coverage));
    }

    [Fact]
    public void HolesStayOpen()
    {
        var builder = Build(lighting => lighting.AddOccluder(
        [
            new(1, 1), new(7, 1), new(7, 5), new(1, 5), new(1, 1),
            new(3, 2), new(3, 4), new(5, 4), new(5, 2), new(3, 2)
        ], 1, false));

        Assert.Equal([0, 255, 255, 0, 0, 255, 255, 0], Row(builder, 2));
    }

    [Fact]
    public void OverlappingAndTouchingOccludersCountOnceWithoutSeams()
    {
        var builder = Build(lighting =>
        {
            lighting.AddOccluder(Rectangle(1, 1, 4.5f, 3), 1, false);
            lighting.AddOccluder(Rectangle(4.5f, 1, 7, 3), 1, false);
            lighting.AddOccluder(Rectangle(2, 0, 3, 4), 1, false);
        });

        Assert.Equal([0, 255, 255, 255, 255, 255, 255, 0], Row(builder, 1));
        Assert.Equal([0, 0, 255, 0, 0, 0, 0, 0], Row(builder, 0));
    }

    [Fact]
    public void OccludersThatShadowThemselvesAreLeftOut()
    {
        var builder = new OccluderMaskBuilder();
        var lighting = new LightingFrame();
        lighting.AddOccluder(Rectangle(1, 1, 6, 3), 1, true);

        Assert.False(builder.Build(lighting, Matrix3x2.Identity, 8, 4));
        Assert.All(builder.Data.ToArray(), coverage => Assert.Equal(0, coverage));
    }

    [Fact]
    public void OccludersReachingPastTheMapStillCoverIt()
    {
        var builder = Build(lighting => lighting.AddOccluder(Rectangle(-20, 1, 3, 30), 1, false));

        Assert.Equal([255, 255, 255, 0, 0, 0, 0, 0], Row(builder, 1));
        Assert.Equal(Row(builder, 1), Row(builder, 3));
        Assert.All(Row(builder, 0), coverage => Assert.Equal(0, coverage));
    }

    [Fact]
    public void TheTransformPlacesWorldPositionsOnTheMap()
    {
        var builder = new OccluderMaskBuilder();
        var lighting = new LightingFrame();
        lighting.AddOccluder(Rectangle(100, 100, 108, 120), 1, false);

        // Half a pixel per world unit, with the map's corner at (100, 100).
        Assert.True(builder.Build(lighting, Matrix3x2.CreateTranslation(-100, -100) * Matrix3x2.CreateScale(0.5f), 8, 12));
        Assert.Equal([255, 255, 255, 255, 0, 0, 0, 0], Row(builder, 9));
        Assert.All(Row(builder, 10), coverage => Assert.Equal(0, coverage));
    }

    [Fact]
    public void SlantedEdgesCoverWhatTheyEnclose()
    {
        // A right triangle with legs of 4 pixels: its diagonal pixels are half covered.
        var builder = Build(lighting => lighting.AddOccluder([new(0, 0), new(4, 4), new(0, 4)], 1, false));

        Assert.Equal([128, 0, 0, 0, 0, 0, 0, 0], Row(builder, 0));
        Assert.Equal([255, 255, 255, 128, 0, 0, 0, 0], Row(builder, 3));
    }

    private static OccluderMaskBuilder Build(Action<LightingFrame> add)
    {
        var lighting = new LightingFrame();
        add(lighting);
        var builder = new OccluderMaskBuilder();
        Assert.True(builder.Build(lighting, Matrix3x2.Identity, 8, 6));
        return builder;
    }

    private static Vector2[] Rectangle(float left, float top, float right, float bottom) =>
        [new(left, top), new(right, top), new(right, bottom), new(left, bottom)];

    private static byte[] Row(OccluderMaskBuilder builder, int y) => builder.Data.Slice(y * builder.Width, builder.Width).ToArray();
}
