using System.Text.Json.Nodes;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Tests.Documents;

public sealed class SceneDocumentModelTests
{
    private readonly UndoService _undo = new(new Undo.ManualTime());
    private readonly SceneDocumentModel _model;
    private readonly List<SceneChange> _changes = [];

    public SceneDocumentModelTests()
    {
        _model = new SceneDocumentModel(SceneDocumentService.CreateDefaultScene(), "scenes/test.tscene", _undo);
        _model.Changed += (_, e) => _changes.Add(e.Change);
    }

    private string Json => DocumentSerializer.Write(_model.Document);

    [Fact]
    public void CreatingAnEntityAddsItAndUndoRemovesIt()
    {
        var before = Json;
        var hero = _model.CreateEntity("Hero");

        Assert.Same(hero, _model.Find(hero.Id));
        Assert.Contains(_changes, c => c.Kind == SceneChangeKind.EntityAdded && c.Entity == hero.Id);
        Assert.True(_model.IsDirty);

        _undo.Undo();
        Assert.Null(_model.Find(hero.Id));
        Assert.Equal(before, Json);
        Assert.False(_model.IsDirty);
    }

    [Fact]
    public void ChildrenFollowTheirParentInTheDocument()
    {
        var root = _model.CreateEntity("Root");
        var child = _model.CreateEntity("Child", root.Id);
        var other = _model.CreateEntity("Other");
        var grandchild = _model.CreateEntity("Grandchild", child.Id);

        Assert.Equal(["Main Camera", "Root", "Child", "Grandchild", "Other"], _model.Entities.Select(e => e.Name));
        Assert.Equal([child.Id, grandchild.Id], _model.GetSubtree(child.Id).Select(e => e.Id));
        _ = other;
    }

    [Fact]
    public void DeletingRemovesDescendantsAndUndoRestoresTheirPlace()
    {
        var root = _model.CreateEntity("Root");
        var child = _model.CreateEntity("Child", root.Id);
        _model.CreateEntity("Grandchild", child.Id);
        _model.CreateEntity("After");
        var before = Json;
        _changes.Clear();

        _model.DeleteEntities([root.Id, child.Id]);

        Assert.Equal(["Main Camera", "After"], _model.Entities.Select(e => e.Name));
        Assert.Equal(3, _changes.Count(c => c.Kind == SceneChangeKind.EntityRemoved));
        Assert.Equal(root.Id, _changes.Last().Entity);
        _undo.Undo();
        Assert.Equal(before, Json);
        _undo.Redo();
        Assert.Equal(2, _model.Entities.Count);
    }

    [Fact]
    public void DuplicatesGetNewIdsAndNumberedNamesNextToTheOriginal()
    {
        var root = _model.CreateEntity("Crate");
        var child = _model.CreateEntity("Lid", root.Id);
        _model.CreateEntity("After");

        var copies = _model.DuplicateEntities([root.Id]);

        var copy = _model.Get(Assert.Single(copies));
        Assert.Equal("Crate 1", copy.Name);
        Assert.NotEqual(root.Id, copy.Id);
        Assert.Equal(["Main Camera", "Crate", "Lid", "Crate 1", "Lid", "After"], _model.Entities.Select(e => e.Name));
        var copiedLid = _model.GetChildren(copy.Id).Single();
        Assert.NotEqual(child.Id, copiedLid.Id);
        _undo.Undo();
        Assert.Equal(4, _model.Entities.Count);
    }

    [Fact]
    public void MovingReparentsAndReordersWithUndo()
    {
        var a = _model.CreateEntity("A");
        var b = _model.CreateEntity("B");
        var c = _model.CreateEntity("C", b.Id);
        var before = Json;

        _model.MoveEntity(a.Id, b.Id, 0);
        Assert.Equal(["Main Camera", "B", "A", "C"], _model.Entities.Select(e => e.Name));
        Assert.Equal(b.Id, _model.Get(a.Id).Parent);
        Assert.Contains(_changes, ch => ch.Kind == SceneChangeKind.EntityMoved && ch.Entity == a.Id && ch.OldParent == null && ch.NewParent == b.Id);

        _model.MoveEntity(c.Id, null, 0);
        Assert.Equal(["C", "Main Camera", "B", "A"], _model.Entities.Select(e => e.Name));

        _undo.Undo();
        _undo.Undo();
        Assert.Equal(before, Json);
    }

    [Fact]
    public void AnEntityCannotMoveIntoItsOwnSubtree()
    {
        var a = _model.CreateEntity("A");
        var b = _model.CreateEntity("B", a.Id);

        Assert.Throws<InvalidOperationException>(() => _model.MoveEntity(a.Id, b.Id));
    }

    [Fact]
    public void PropertyChangesStoreOnlyThePathAndMergeConsecutiveEdits()
    {
        var hero = _model.CreateEntity("Hero");
        _undo.Seal();
        _changes.Clear();

        _model.SetProperty(hero.Id, "Transform", "position", new JsonArray(10, 0));
        _model.SetProperty(hero.Id, "Transform", "position", new JsonArray(20, 0));
        _model.SetProperty(hero.Id, "Transform", "position", new JsonArray(30, 5));

        Assert.Equal(2, _undo.UndoSteps.Count);
        Assert.Equal(3, _changes.Count(c => c is { Kind: SceneChangeKind.PropertyChanged, Component: "Transform", Property: "position" }));
        Assert.Equal("[30,5]", _model.GetProperty(hero.Id, "Transform", "position")!.ToJsonString());
        _undo.Undo();
        Assert.Equal("[0,0]", _model.GetProperty(hero.Id, "Transform", "position")!.ToJsonString());
    }

    [Fact]
    public void SettingTheSameValueRecordsNothing()
    {
        var hero = _model.CreateEntity("Hero");
        var steps = _undo.UndoSteps.Count;

        _model.SetProperty(hero.Id, "Transform", "position", new JsonArray(0, 0));

        Assert.Equal(steps, _undo.UndoSteps.Count);
    }

    [Fact]
    public void NestedPropertyPathsAreCreatedAndRemovedOnUndo()
    {
        var hero = _model.CreateEntity("Hero");
        _model.AddComponent(hero.Id, new ComponentDocument("Sprite", []));

        _model.SetProperty(hero.Id, "Sprite", "source", new JsonArray(0, 0, 16, 16));
        Assert.NotNull(_model.GetProperty(hero.Id, "Sprite", "source"));
        _undo.Undo();
        Assert.Null(_model.GetProperty(hero.Id, "Sprite", "source"));
    }

    [Fact]
    public void ComponentsAreAddedAndRemovedWithUndo()
    {
        var hero = _model.CreateEntity("Hero");
        _model.AddComponent(hero.Id, new ComponentDocument("Sprite", new JsonObject { ["layer"] = 5 }), 0);
        Assert.Equal("Sprite", _model.Get(hero.Id).Components[0].Type);

        _model.RemoveComponent(hero.Id, "Sprite");
        Assert.Null(_model.Get(hero.Id).FindComponent("Sprite"));

        _undo.Undo();
        var sprite = _model.Get(hero.Id).Components[0];
        Assert.Equal("Sprite", sprite.Type);
        Assert.Equal(5, sprite.Data["layer"]!.GetValue<int>());
        _undo.Undo();
        Assert.Null(_model.Get(hero.Id).FindComponent("Sprite"));
    }

    [Fact]
    public void RenameAndFlagsAreUndoable()
    {
        var hero = _model.CreateEntity("Hero");
        _model.Rename(hero.Id, "Heroine");
        _model.SetActive(hero.Id, false);
        _model.SetLocked(hero.Id, true);

        Assert.Equal("Heroine", hero.Name);
        Assert.False(hero.Active);
        Assert.True(hero.Editor.Locked);
        Assert.Contains(_changes, c => c is { Kind: SceneChangeKind.EntityStateChanged, Property: "locked" });

        _undo.Undo();
        _undo.Undo();
        _undo.Undo();
        Assert.Equal("Hero", hero.Name);
        Assert.True(hero.Active);
        Assert.False(hero.Editor.Locked);
    }

    [Fact]
    public void ATransactionUndoesSeveralOperationsAtOnce()
    {
        var before = Json;
        using (_undo.BeginTransaction("Build a house"))
        {
            var house = _model.CreateEntity("House");
            _model.CreateEntity("Door", house.Id);
            _model.SetProperty(house.Id, "Transform", "position", new JsonArray(64, 64));
        }

        _undo.Undo();
        Assert.Equal(before, Json);
    }

    [Fact]
    public void InsertingEntitiesKeepsTheirOwnHierarchyAndAdoptsTheRest()
    {
        var parent = _model.CreateEntity("Parent");
        var a = new EntityDocument { Id = Guid.NewGuid(), Name = "Pasted" };
        var b = new EntityDocument { Id = Guid.NewGuid(), Name = "Pasted child", Parent = a.Id };

        var roots = _model.InsertEntities([a, b], parent.Id);

        Assert.Equal([a.Id], roots);
        Assert.Equal(parent.Id, _model.Get(a.Id).Parent);
        Assert.Equal(a.Id, _model.Get(b.Id).Parent);
    }
}
