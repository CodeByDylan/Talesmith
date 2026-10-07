namespace Talesmith.Lighting.Tests;

public sealed class LightAnimationTests
{
    [Theory]
    [InlineData(LightAnimation.Flicker)]
    [InlineData(LightAnimation.Pulse)]
    public void AnimationsStayWithinTheirAmount(LightAnimation animation)
    {
        var factors = Enumerable.Range(1, 500).Select(i => LightAnimations.Factor(animation, i * 0.013f, 0.4f, 7)).ToList();

        Assert.All(factors, f => Assert.InRange(f, 0.6f - 1e-5f, 1 + 1e-5f));
        Assert.True(factors.Max() - factors.Min() > 0.2f);
    }

    [Fact]
    public void UnplayedAndDisabledAnimationsLeaveTheIntensity()
    {
        Assert.Equal(1, LightAnimations.Factor(LightAnimation.Flicker, 0, 0.5f, 3));
        Assert.Equal(1, LightAnimations.Factor(LightAnimation.None, 2.5f, 0.5f, 3));
    }

    [Fact]
    public void FlickersDifferBetweenLights() =>
        Assert.NotEqual(LightAnimations.Factor(LightAnimation.Flicker, 1.3f, 0.5f, 1), LightAnimations.Factor(LightAnimation.Flicker, 1.3f, 0.5f, 2));
}
