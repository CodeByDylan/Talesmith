using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Tests.Maps;

public sealed class MapValueTests
{
    [Fact]
    public void PropertySetsChangeByCopyAndKeepTheirOrder()
    {
        var properties = PropertySet.Empty
            .With("title", PropertyValue.FromString("Isles"))
            .With("difficulty", PropertyValue.FromInt(3))
            .With("fog", PropertyValue.FromBool(true));

        var replaced = properties.With("difficulty", PropertyValue.FromInt(4));
        var removed = replaced.Without("title");
        var renamed = removed.Renamed("fog", "mist");

        Assert.Equal(["title", "difficulty", "fog"], replaced.Values.Keys);
        Assert.Equal(3, properties.GetInt("difficulty"));
        Assert.Equal(4, replaced.GetInt("difficulty"));
        Assert.Equal(["difficulty", "mist"], renamed.Values.Keys);
        Assert.True(renamed.GetBool("mist"));
        Assert.Same(properties, properties.With("fog", PropertyValue.FromBool(true)));
        Assert.True(properties.SequenceEquals(new PropertySet(properties.Values)));
        Assert.False(properties.SequenceEquals(replaced));
    }

    [Fact]
    public void PropertyValuesAreStoredAsInvariantText()
    {
        Assert.Equal("1.25", PropertyValue.FromFloat(1.25f).Raw);
        Assert.Equal("#FF8000", PropertyValue.FromColor(new Mathematics.Color(255, 128, 0)).Raw);
        Assert.Equal(new PropertyValue(PropertyType.Color, "#FFFFFFFF"), PropertyValue.Default(PropertyType.Color));
    }

    [Theory]
    [InlineData(0, false, 1, 6, 1, false)]
    [InlineData(5, false, 1, 6, 0, false)]
    [InlineData(3, true, -1, 4, 2, true)]
    public void TileCellsRotateWithinOneTurn(int rotation, bool flip, int steps, int rotationSteps, int expectedRotation, bool expectedFlip)
    {
        var cell = new TileCell(2, 40, rotation, flip).Rotate(steps, rotationSteps);

        Assert.Equal((2, 40, expectedRotation, expectedFlip), (cell.TilesetId, cell.TileId, cell.Rotation, cell.FlipX));
    }

    [Fact]
    public void FlippingTwiceRestoresATile()
    {
        foreach (var steps in new[] { 4, 6 })
        {
            for (var rotation = 0; rotation < steps; rotation++)
            {
                var cell = new TileCell(1, 3, rotation);
                Assert.Equal(cell, cell.FlipHorizontal(steps).FlipHorizontal(steps));
                Assert.Equal(cell, cell.FlipVertical(steps).FlipVertical(steps));
                Assert.Equal(cell.FlipHorizontal(steps).Rotate(steps / 2, steps), cell.FlipVertical(steps));
            }
        }

        Assert.Equal(TileCell.Empty, TileCell.Empty.FlipHorizontal(6));
        Assert.Equal(new TileCell(1, 3), new TileCell(1, 3, 2, true).WithoutTransform);
    }

    [Theory]
    [InlineData("collision", true, LayerRole.Collision)]
    [InlineData("Triggers", true, LayerRole.Trigger)]
    [InlineData(" navigation ", true, LayerRole.Navigation)]
    [InlineData("objects", true, LayerRole.Object)]
    [InlineData("3", false, LayerRole.Ground)]
    [InlineData("walls", false, LayerRole.Ground)]
    public void RolesParseFromTheirNames(string text, bool parses, LayerRole role)
    {
        Assert.Equal(parses, LayerRoles.TryParse(text, out var parsed));
        if (parses)
            Assert.Equal(role, parsed);
    }

    [Theory]
    [InlineData("Collision", false, LayerRole.Collision)]
    [InlineData("Level collisions", false, LayerRole.Collision)]
    [InlineData("Triggers", true, LayerRole.Trigger)]
    [InlineData("nav_mesh", false, LayerRole.Navigation)]
    [InlineData("Navigation", false, LayerRole.Navigation)]
    [InlineData("Decoration 2", false, LayerRole.Decoration)]
    [InlineData("Terrain", false, LayerRole.Ground)]
    [InlineData("Props", true, LayerRole.Object)]
    public void RolesAreInferredFromObviousLayerNames(string name, bool isObjectLayer, LayerRole role) =>
        Assert.Equal(role, LayerRoles.Infer(name, isObjectLayer));
}
