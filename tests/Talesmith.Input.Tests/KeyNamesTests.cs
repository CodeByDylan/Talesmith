namespace Talesmith.Input.Tests;

public sealed class KeyNamesTests
{
    [Theory]
    [InlineData("A", Key.A)]
    [InlineData("space", Key.Space)]
    [InlineData(" Esc ", Key.Escape)]
    [InlineData("7", Key.D7)]
    [InlineData("num7", Key.NumPad7)]
    [InlineData("Ctrl", Key.LeftControl)]
    [InlineData("AltGr", Key.RightAlt)]
    [InlineData("Win", Key.LeftMeta)]
    [InlineData("PgDn", Key.PageDown)]
    [InlineData("`", Key.Grave)]
    [InlineData("=", Key.Plus)]
    [InlineData("[", Key.OpenBracket)]
    public void NamesAndAliasesParseIgnoringCase(string name, Key expected)
    {
        Assert.True(KeyNames.TryParse(name, out var key));
        Assert.Equal(expected, key);
        Assert.Equal(expected, KeyNames.Parse(name));
    }

    [Fact]
    public void EveryKeyParsesFromItsOwnName()
    {
        foreach (var key in Enum.GetValues<Key>())
            Assert.Equal(key, KeyNames.Parse(key.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Hyper")]
    [InlineData("F13")]
    public void UnknownNamesAreRejected(string? name)
    {
        Assert.False(KeyNames.TryParse(name, out var key));
        Assert.Equal(Key.None, key);
        Assert.Throws<FormatException>(() => KeyNames.Parse(name!));
    }
}
