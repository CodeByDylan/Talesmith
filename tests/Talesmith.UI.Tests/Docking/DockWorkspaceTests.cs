using Talesmith.UI.Docking;

namespace Talesmith.UI.Tests.Docking;

public sealed class DockWorkspaceTests
{
    [Fact]
    public void SpaceIsSharedByWeightInWholePixels()
    {
        Assert.Equal([300, 700], DockWorkspace.Distribute(1000, [0.3, 0.7], [100, 100]));
        Assert.Equal([333, 333, 334], DockWorkspace.Distribute(1000, [1, 1, 1], [0, 0, 0]));
    }

    [Fact]
    public void PartsKeepTheirMinimumWhileThereIsRoomForEveryMinimum()
    {
        Assert.Equal([100, 900], DockWorkspace.Distribute(1000, [0.02, 0.98], [100, 100]));
        Assert.Equal([450, 100, 450], DockWorkspace.Distribute(1000, [0.48, 0.04, 0.48], [100, 100, 100]));
    }

    [Fact]
    public void PartsShrinkAlikeWhenTheSpaceIsSmallerThanTheirMinimums()
    {
        Assert.Equal([75, 75], DockWorkspace.Distribute(150, [0.9, 0.1], [100, 100]));
        Assert.Equal([0, 0], DockWorkspace.Distribute(0, [1, 1], [100, 100]));
    }

    [Fact]
    public void ASplitNeedsTheMinimumsOfItsShownChildren()
    {
        var split = new DockSplit("root", DockOrientation.Horizontal, new DockGroup("a", "one"), new DockGroup("b", "two"), new DockGroup("c", "three"));
        var minimum = DockWorkspace.MinimumGroupSize;

        Assert.Equal(new Avalonia.Size(minimum.Width * 3 + DockWorkspace.Gap * 2, minimum.Height), DockWorkspace.Minimum(split));
    }
}
