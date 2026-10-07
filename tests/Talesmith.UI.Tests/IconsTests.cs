
namespace Talesmith.UI.Tests;

public sealed class IconsTests
{
    [Theory]
    [InlineData("camera")]
    [InlineData("list-tree")]
    [InlineData("ListTree")]
    [InlineData("VOLUME")]
    [InlineData("rotate-3d")]
    [InlineData("maximize-2")]
    [InlineData("entity")]
    [InlineData("prefab")]
    [InlineData("script")]
    public void FindResolvesNamesAndAliases(string name)
    {
        Assert.NotNull(Headless.Run(() => Icons.Find(name)));
    }

    [Fact]
    public void FindReturnsNullForUnknownNames()
    {
        Assert.Null(Headless.Run(() => Icons.Find("no-such-icon")));
        Assert.Null(Headless.Run(() => Icons.Find(null)));
        Assert.Null(Headless.Run(() => Icons.Find(" ")));
    }

    [Theory]
    [InlineData("list-tree")]
    [InlineData("ListTree")]
    [InlineData("script")]
    [InlineData("no-such-icon")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task ExistsAgreesWithFindOnAnyThread(string? name)
    {
        var exists = await Task.Run(() => Icons.Exists(name), TestContext.Current.CancellationToken);

        Assert.Equal(Headless.Run(() => Icons.Find(name)) is not null, exists);
    }

    [Fact]
    public void NamesAreKebabCaseAndResolvable()
    {
        Assert.Contains("list-tree", Icons.Names);
        Assert.Contains("rotate-3d", Icons.Names);
        Assert.Contains("volume-off", Icons.Names);
        Headless.Run(() =>
        {
            Assert.All(Icons.Names, name => Assert.Same(Icons.Find(name), Icons.Find(name)));
            Assert.All(Icons.Names, name => Assert.NotNull(Icons.Find(name)));
        });
    }
}
