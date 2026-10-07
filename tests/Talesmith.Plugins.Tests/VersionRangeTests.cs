namespace Talesmith.Plugins.Tests;

public sealed class VersionRangeTests
{
    [Theory]
    [InlineData("*", "0.0.1", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData("1.2", "1.2.9", true)]
    [InlineData("1.2", "1.3.0", false)]
    [InlineData("1", "1.9.9", true)]
    [InlineData("1", "2.0.0", false)]
    [InlineData("1.x", "1.4.0", true)]
    [InlineData("1.2.x", "1.3.0", false)]
    [InlineData(">=1.2 <2.0", "1.2.0", true)]
    [InlineData(">=1.2 <2.0", "1.9.9", true)]
    [InlineData(">=1.2 <2.0", "2.0.0", false)]
    [InlineData(">=1.2 <2.0", "1.1.9", false)]
    [InlineData(">= 1.2  < 2.0", "1.5.0", true)]
    [InlineData(">1.2", "1.2.9", false)]
    [InlineData(">1.2", "1.3.0", true)]
    [InlineData(">1.2.3", "1.2.4", true)]
    [InlineData("<=1.2", "1.2.9", true)]
    [InlineData("<=1.2", "1.3.0", false)]
    [InlineData("<=1.2.3", "1.2.3", true)]
    [InlineData("^1.2", "1.9.0", true)]
    [InlineData("^1.2", "2.0.0", false)]
    [InlineData("^1.2", "1.1.0", false)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("^0", "0.9.0", true)]
    [InlineData("~1.2.3", "1.2.9", true)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData("~1", "1.9.0", true)]
    [InlineData("1.0 || ^3.0", "3.4.0", true)]
    [InlineData("1.0 || ^3.0", "2.0.0", false)]
    public void MatchesVersionsLikeNpmRanges(string range, string version, bool expected)
    {
        Assert.Equal(expected, VersionRange.Parse(range).IsSatisfiedBy(Version.Parse(version)));
    }

    [Fact]
    public void ComparesTwoThreeAndFourPartVersionsAsEqual()
    {
        var range = VersionRange.Parse("0.1.0");
        Assert.True(range.IsSatisfiedBy(new Version(0, 1)));
        Assert.True(range.IsSatisfiedBy(new Version(0, 1, 0, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("banana")]
    [InlineData(">=")]
    [InlineData("1.2.3.4")]
    [InlineData("1.0 ||")]
    [InlineData(">=-1")]
    public void RejectsMalformedRangesWithAReason(string text)
    {
        Assert.False(VersionRange.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void FormatsNormalizedText()
    {
        Assert.Equal(">=1.2 <2.0", VersionRange.Parse("  >= 1.2   <2.0 ").ToString());
        Assert.Equal(">=1.0.0", VersionRange.AtLeast(new Version(1, 0)).ToString());
        Assert.True(VersionRange.Any.IsAny);
        Assert.Equal(VersionRange.Parse("^1.0"), VersionRange.Parse("^1.0"));
    }
}
