using Avalonia.Input;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class NumberFieldMathTests
{
    [Theory]
    [InlineData("42", 42)]
    [InlineData("16*2+4", 36)]
    [InlineData("16 * (2 + 4)", 96)]
    [InlineData("-3 + 5", 2)]
    [InlineData("--3", 3)]
    [InlineData("10 / 4", 2.5)]
    [InlineData("10 % 4", 2)]
    [InlineData("2^3^2", 512)]
    [InlineData("-2^2", -4)]
    [InlineData("1,5 * 2", 3)]
    [InlineData(".5 + .25", 0.75)]
    [InlineData("  7  ", 7)]
    public void TryEvaluateComputesArithmetic(string text, double expected)
    {
        Assert.True(NumberExpression.TryEvaluate(text, out var value));
        Assert.Equal(expected, value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("1 / 0")]
    [InlineData("1..2")]
    [InlineData(".")]
    [InlineData("2 3")]
    public void TryEvaluateRejectsInvalidInput(string text)
    {
        Assert.False(NumberExpression.TryEvaluate(text, out _));
    }

    [Theory]
    [InlineData(KeyModifiers.None, 1)]
    [InlineData(KeyModifiers.Shift, 0.1)]
    [InlineData(KeyModifiers.Control, 10)]
    [InlineData(KeyModifiers.Control | KeyModifiers.Shift, 0.1)]
    public void FactorFollowsModifiers(KeyModifiers modifiers, double expected)
    {
        Assert.Equal(expected, NumberScrub.Factor(modifiers));
    }

    [Fact]
    public void DragMovesOneStepPerPixelAndClamps()
    {
        Assert.Equal(15, NumberScrub.Drag(10, 5, 1, 1, double.NegativeInfinity, double.PositiveInfinity));
        Assert.Equal(0.6, NumberScrub.Drag(0.5, 10, 0.01, 1, 0, 1));
        Assert.Equal(1, NumberScrub.Drag(0.5, 500, 0.01, 1, 0, 1));
        Assert.Equal(0, NumberScrub.Drag(0.5, -500, 0.01, 1, 0, 1));
    }

    [Fact]
    public void DragSnapsToTheEffectiveStep()
    {
        Assert.Equal(30, NumberScrub.Drag(12, 2, 1, NumberScrub.CoarseFactor, double.MinValue, double.MaxValue));
        Assert.Equal(12.3, NumberScrub.Drag(12, 3, 1, NumberScrub.FineFactor, double.MinValue, double.MaxValue));
    }

    [Fact]
    public void StepAvoidsFloatingPointNoise()
    {
        Assert.Equal(0.3, NumberScrub.Step(0.2, 1, 0.1, 1, 0, 1));
        Assert.Equal(-0.7, NumberScrub.Step(0.3, -1, 1, 1, double.MinValue, double.MaxValue));
        Assert.Equal(5, NumberScrub.Step(4.5, 1, 1, 1, 0, 5));
    }
}
