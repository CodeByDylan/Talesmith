using Talesmith.Assets;
using Talesmith.Editor.Selection;

namespace Talesmith.Editor.Tests.Selection;

public sealed class SelectionServiceTests
{
    private readonly SelectionService _selection = new();
    private readonly List<SelectionKinds> _changes = [];

    public SelectionServiceTests() => _selection.Changed += (_, e) => _changes.Add(e.Kinds);

    [Fact]
    public void ReplacingSelectsOnlyTheGivenItemsOfAnyKind()
    {
        var a = Guid.NewGuid();
        var asset = AssetGuid.NewGuid();
        _selection.SelectEntity(a);
        _selection.SelectAsset(asset);

        Assert.Empty(_selection.Entities);
        Assert.Equal([asset], _selection.Assets);
        Assert.Equal(asset, _selection.Primary);
        Assert.Equal(SelectionKinds.Entities | SelectionKinds.Assets, _changes[^1]);
    }

    [Fact]
    public void AddingAndTogglingKeepOtherItemsAndOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        _selection.SelectEntity(a);
        _selection.SelectEntity(b, SelectionMode.Add);
        Assert.Equal([a, b], _selection.Entities);
        Assert.Equal(b, _selection.Primary);

        _selection.SelectEntity(a, SelectionMode.Toggle);
        Assert.Equal([b], _selection.Entities);
        _selection.SelectEntity(a, SelectionMode.Toggle);
        Assert.Equal([b, a], _selection.Entities);
        _selection.SelectEntity(b, SelectionMode.Remove);
        Assert.Equal([a], _selection.Entities);
    }

    [Fact]
    public void SelectingTheSameItemsAgainRaisesNothing()
    {
        var a = Guid.NewGuid();
        _selection.SelectEntity(a);
        _changes.Clear();

        _selection.SelectEntity(a);
        _selection.SelectEntity(a, SelectionMode.Add);

        Assert.Empty(_changes);
    }

    [Fact]
    public void ObjectsAreSelectedAlongsideOtherKindsWhenAdded()
    {
        var tile = new object();
        var a = Guid.NewGuid();
        _selection.SelectEntity(a);
        _selection.SelectObjects([tile], SelectionMode.Add);

        Assert.True(_selection.IsSelected(tile));
        Assert.True(_selection.IsSelected(a));
        _selection.Clear();
        Assert.True(_selection.IsEmpty);
        Assert.Null(_selection.Primary);
    }
}
