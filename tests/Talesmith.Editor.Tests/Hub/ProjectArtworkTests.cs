using Avalonia.Media;
using Talesmith.Editor.Hub;

namespace Talesmith.Editor.Tests.Hub;

public sealed class ProjectArtworkTests
{
    [Theory]
    [InlineData("Hex Quest", "#FBBF24")]
    [InlineData("hex quest", "#FBBF24")]
    [InlineData("Isle Hopper", "#38BDF8")]
    public void AProjectGetsTheSameGradientInEverySession(string name, string from) => Headless.Run(() =>
    {
        var gradient = Assert.IsType<LinearGradientBrush>(ProjectArtwork.Gradient(name));

        Assert.Equal(Color.Parse(from), gradient.GradientStops[0].Color);
    });
}
