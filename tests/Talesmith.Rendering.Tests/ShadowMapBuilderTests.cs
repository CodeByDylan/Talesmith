using System.Numerics;
using Talesmith.Rendering.Lighting;

namespace Talesmith.Rendering.Tests;

public sealed class ShadowMapBuilderTests
{
    private static readonly Vector2[] Box = Rectangle(90, 110);

    [Fact]
    public void RowsStoreTheDistanceToTheFarSideOfShapesThatDoNotShadowThemselves()
    {
        var lighting = Lighting(selfShadows: false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(1, builder.RowCount);
        Assert.Equal(0.55f, DepthAt(builder, 0), 2);
        Assert.Equal(1f, DepthAt(builder, MathF.PI / 2), 2);
    }

    [Fact]
    public void SelfShadowingShapesBlockFromTheirNearSide()
    {
        var builder = new ShadowMapBuilder();
        builder.Build(Lighting(selfShadows: true));

        Assert.Equal(0.45f, DepthAt(builder, 0), 2);
    }

    [Fact]
    public void TouchingShapesActAsOneShape()
    {
        var lighting = Lighting(selfShadows: false);
        lighting.AddOccluder(Rectangle(110, 130), 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(0.65f, DepthAt(builder, 0), 2);
    }

    [Fact]
    public void OverlappingShapesActAsOneShape()
    {
        var lighting = Lighting(selfShadows: false);
        lighting.AddOccluder(Rectangle(100, 130), 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(0.65f, DepthAt(builder, 0), 2);
    }

    [Fact]
    public void ShadowsStartWhereTheLightLeavesTheFirstShapeItPassesThrough()
    {
        // Two pixels of air between the shapes: the second one is in the first one's shadow.
        var lighting = Lighting(selfShadows: false);
        lighting.AddOccluder(Rectangle(112, 130), 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(0.55f, DepthAt(builder, 0), 2);
    }

    [Fact]
    public void ShadowsStartAtTheFirstWayOutOfAHollowShape()
    {
        // The ray along y = 0 crosses the shape's left wall, the hollow inside it and its right wall.
        var lighting = new LightingFrame();
        lighting.Enable(LightMapSettings.Default with { ShadowResolution = 360 });
        lighting.AddLight(Light());
        lighting.AddOccluder([new(90, -30), new(170, -30), new(170, 30), new(90, 30), new(90, -30), new(110, -20), new(110, 20), new(150, 20),
            new(150, -20), new(110, -20)], 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(0.55f, DepthAt(builder, 0), 2);
    }

    [Fact]
    public void LightsInsideAShapeAreShadowedWhereTheyLeaveIt()
    {
        var lighting = new LightingFrame();
        lighting.Enable(LightMapSettings.Default with { ShadowResolution = 360 });
        lighting.AddLight(Light());
        lighting.AddOccluder(Rectangle(-10, 10), 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(0.05f, DepthAt(builder, 0), 2);
        Assert.Equal(0.05f, DepthAt(builder, MathF.PI), 2);
    }

    [Fact]
    public void DirectionalLightsTreatTouchingShapesAsOne()
    {
        var lighting = new LightingFrame();
        lighting.Enable(LightMapSettings.Default with { ShadowResolution = 256 });
        lighting.AddLight(new FrameLight
        {
            Type = LightType.Directional,
            Position = new Vector2(100, 0),
            Direction = Vector2.UnitX,
            Radius = 100,
            Color = Vector3.One,
            CastsShadows = true,
            ShadowStrength = 1,
            ShadowLayers = uint.MaxValue
        });
        lighting.AddOccluder(Rectangle(40, 60), 1, false);
        lighting.AddOccluder(Rectangle(60, 80), 1, false);
        lighting.AddOccluder(Rectangle(120, 140), 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        // The column through y = 0 is in the middle; depths run from x = 0 to x = 200.
        Assert.Equal(0.4f, (float)builder.Data[builder.Width / 2], 2);
    }

    [Fact]
    public void LightsWithoutShadowsGetNoRow()
    {
        var lighting = Lighting(selfShadows: false);
        lighting.AddLight(new FrameLight { Type = LightType.Point, Radius = 100, Color = Vector3.One });
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(0, builder.RowOf(0));
        Assert.Equal(-1, builder.RowOf(1));
    }

    [Fact]
    public void ShadowLayersFilterOccluders()
    {
        var lighting = new LightingFrame();
        lighting.Enable(LightMapSettings.Default with { ShadowResolution = 360 });
        lighting.AddLight(Light() with { ShadowLayers = 2 });
        lighting.AddOccluder(Box, 1, false);
        var builder = new ShadowMapBuilder();
        builder.Build(lighting);

        Assert.Equal(1f, DepthAt(builder, 0), 2);
    }

    private static Vector2[] Rectangle(float left, float right) => [new(left, -10), new(right, -10), new(right, 10), new(left, 10)];

    private static LightingFrame Lighting(bool selfShadows)
    {
        var lighting = new LightingFrame();
        lighting.Enable(LightMapSettings.Default with { ShadowResolution = 360 });
        lighting.AddLight(Light());
        lighting.AddOccluder(Box, 1, selfShadows);
        return lighting;
    }

    private static FrameLight Light() => new()
    {
        Type = LightType.Point,
        Position = Vector2.Zero,
        Radius = 200,
        Color = Vector3.One,
        CastsShadows = true,
        ShadowStrength = 1,
        ShadowLayers = uint.MaxValue
    };

    private static float DepthAt(ShadowMapBuilder builder, float angle)
    {
        var s = angle / MathF.Tau + 0.5f;
        var x = Math.Min((int)(s * builder.Width), builder.Width - 1);
        return (float)builder.Data[x];
    }
}
