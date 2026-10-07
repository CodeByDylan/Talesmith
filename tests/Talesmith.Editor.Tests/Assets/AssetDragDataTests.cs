using Talesmith.Assets;
using Talesmith.Editor.Assets;

namespace Talesmith.Editor.Tests.Assets;

public sealed class AssetDragDataTests
{
    [Fact]
    public void GuidsRoundTripThroughTheDragPayload()
    {
        AssetGuid[] assets = [AssetGuid.NewGuid(), AssetGuid.NewGuid(), AssetGuid.NewGuid()];

        var data = AssetDragData.Create(assets);

        Assert.True(AssetDragData.Contains(data));
        Assert.Equal(assets, AssetDragData.Read(data));
        Assert.Equal("talesmith.assets", AssetDragData.FormatId);
    }

    [Fact]
    public void TheFormatIsNewlineSeparatedHexGuids()
    {
        var a = AssetGuid.NewGuid();
        var b = AssetGuid.NewGuid();

        var text = AssetDragData.Encode([a, b]);

        Assert.Equal($"{a}\n{b}", text);
        Assert.All(text.Split('\n'), line => Assert.Matches("^[0-9a-f]{32}$", line));
    }

    [Fact]
    public void DecodingSkipsJunkRepeatsAndBlankLines()
    {
        var a = AssetGuid.NewGuid();

        Assert.Equal([a], AssetDragData.Decode($"\r\n{a}\r\nnot a guid\n{a}\n"));
        Assert.Empty(AssetDragData.Decode(null));
        Assert.False(AssetDragData.Contains(null));
    }
}
