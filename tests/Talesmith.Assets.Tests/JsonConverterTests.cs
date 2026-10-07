using System.Numerics;
using System.Text.Json;
using Talesmith.Assets.Json;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Tests;

public sealed class JsonConverterTests
{
    [Fact]
    public void VectorsAreWrittenOnOneLineAndReadAsArraysOrObjects()
    {
        Assert.Equal("[1.5, -2]", JsonSerializer.Serialize(new Vector2(1.5f, -2), AssetJson.Options));
        Assert.Equal(new Vector2(3, 4), JsonSerializer.Deserialize<Vector2>("""{ "x": 3, "y": 4 }""", AssetJson.Options));
        Assert.Equal(new Vector2(3, 4), JsonSerializer.Deserialize<Vector2>("[3, 4]", AssetJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Vector2>("[3]", AssetJson.Options));
    }

    [Fact]
    public void ColorsRoundTripAsHex()
    {
        var color = new Color(0x11, 0x22, 0x33, 0x44);

        Assert.Equal("\"#44112233\"", JsonSerializer.Serialize(color, AssetJson.Options));
        Assert.Equal(color, JsonSerializer.Deserialize<Color>("\"#44112233\"", AssetJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Color>("\"red\"", AssetJson.Options));
    }

    [Fact]
    public void EmptyAssetGuidsAreNull()
    {
        var guid = AssetGuid.NewGuid();

        Assert.Equal("null", JsonSerializer.Serialize(AssetGuid.Empty, AssetJson.Options));
        Assert.Equal(AssetGuid.Empty, JsonSerializer.Deserialize<AssetGuid>("null", AssetJson.Options));
        Assert.Equal(AssetGuid.Empty, JsonSerializer.Deserialize<AssetGuid>("\"\"", AssetJson.Options));
        Assert.Equal(guid, JsonSerializer.Deserialize<AssetGuid>(JsonSerializer.Serialize(guid, AssetJson.Options), AssetJson.Options));
    }

    [Fact]
    public void CurvesRoundTripAndReadEveryForm()
    {
        var curve = new Curve([new CurveKey(0, 1), new CurveKey(1, 2, 0.5f, -0.5f, CurveInterpolation.Linear)]);

        var json = JsonSerializer.Serialize(curve, AssetJson.Options);

        Assert.Equal(curve, JsonSerializer.Deserialize<Curve>(json, AssetJson.Options));
        Assert.Equal(1, json.Split("inTangent").Length - 1);
        Assert.Equal(Curve.Constant(3), JsonSerializer.Deserialize<Curve>("3", AssetJson.Options));
        Assert.Equal(curve, JsonSerializer.Deserialize<Curve>(
            """{ "keys": [{ "time": 0, "value": 1 }, { "time": 1, "value": 2, "inTangent": 0.5, "outTangent": -0.5, "interpolation": "Linear" }] }""",
            AssetJson.Options));
    }

    [Fact]
    public void GradientsRoundTripAndReadEveryForm()
    {
        var gradient = Gradient.Between(Color.Black, Color.White);

        Assert.Equal(gradient, JsonSerializer.Deserialize<Gradient>(JsonSerializer.Serialize(gradient, AssetJson.Options), AssetJson.Options));
        Assert.Equal(Gradient.Solid(Color.White), JsonSerializer.Deserialize<Gradient>("\"#FFFFFF\"", AssetJson.Options));
        Assert.Equal(gradient, JsonSerializer.Deserialize<Gradient>(
            """{ "stops": [{ "position": 0, "color": "#000000" }, { "position": 1, "color": "#FFFFFF" }] }""", AssetJson.Options));
    }
}
