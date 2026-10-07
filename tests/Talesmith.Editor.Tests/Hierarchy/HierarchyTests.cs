using Talesmith.Editor.DragAndDrop;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Tests.Hierarchy;

public sealed class HierarchyTests
{
    [Fact]
    public void DroppingEntitiesReparentsAndReordersAsOneUndoStep() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var hierarchy = editor.Get<HierarchyViewModel>();
        var undo = editor.Get<IUndoService>();
        var camera = model.Entities.Single(e => e.Name == "Main Camera");
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var level = model.Entities.Single(e => e.Name == "Level");
        var heroRow = hierarchy.Tree.Find(hero.Id)!;
        var levelRow = hierarchy.Tree.Find(level.Id)!;

        var drag = Read(hierarchy, [camera.Id, level.Id]);
        Assert.True(hierarchy.Drop(drag, heroRow, DropPosition.Inside));
        Assert.Equal(hero.Id, model.Get(camera.Id).Parent);
        Assert.Equal(hero.Id, model.Get(level.Id).Parent);
        Assert.Equal(0, model.GetSiblingIndex(camera.Id));
        Assert.Equal(heroRow.Depth + 1, hierarchy.Tree.Find(camera.Id)!.Depth);
        Assert.Contains(hierarchy.Tree.Find(camera.Id)!, hierarchy.Rows);

        undo.Undo();
        Assert.Null(model.Get(camera.Id).Parent);
        Assert.Null(model.Get(level.Id).Parent);
        Assert.Equal(0, hierarchy.Tree.Find(camera.Id)!.Depth);

        Assert.True(hierarchy.Drop(Read(hierarchy, [camera.Id]), levelRow, DropPosition.After));
        Assert.Equal(model.GetSiblingIndex(level.Id) + 1, model.GetSiblingIndex(camera.Id));
        Assert.Equal(hierarchy.Rows.IndexOf(levelRow) + 1, hierarchy.Rows.IndexOf(hierarchy.Tree.Find(camera.Id)!));
        undo.Undo();
        Assert.Equal(0, model.GetSiblingIndex(camera.Id));
        Assert.Equal(0, hierarchy.Rows.IndexOf(hierarchy.Tree.Find(camera.Id)!));
    });

    [Fact]
    public void AnEntityCannotDropIntoItsOwnDescendant() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var hierarchy = editor.Get<HierarchyViewModel>();
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var child = model.CreateEntity("Hat", hero.Id);
        hierarchy.Tree.Reveal(child.Id);

        Assert.False(hierarchy.CanDrop(Read(hierarchy, [hero.Id]), hierarchy.Tree.Find(child.Id), DropPosition.Inside));
        Assert.True(hierarchy.CanDrop(Read(hierarchy, [child.Id]), hierarchy.Tree.Find(hero.Id), DropPosition.Before));
    });

    [Fact]
    public void TheTreeFollowsChangesAndTheFilterKeepsAncestors() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var hierarchy = editor.Get<HierarchyViewModel>();
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var created = hierarchy.Create("light.point", hero.Id)!.Value;
        Assert.Equal([created], editor.Get<ISelectionService>().Entities);
        Assert.True(hierarchy.Tree.Find(hero.Id)!.IsExpanded);
        Assert.Contains(model.Get(created).Components, c => c.Type.EndsWith("Light2D", StringComparison.Ordinal));

        model.Rename(created, "Torch");
        Assert.Equal("Torch", hierarchy.Tree.Find(created)!.Name);

        hierarchy.Filter = "torch";
        Assert.Equal(["Hero", "Torch"], hierarchy.Rows.Select(r => r.Name));
        Assert.True(hierarchy.Rows[0].IsContext);
        Assert.Equal("Torch", hierarchy.Rows[1].MatchText);
        hierarchy.Filter = "";

        model.DeleteEntities([created]);
        Assert.Null(hierarchy.Tree.Find(created));
        Assert.False(hierarchy.Tree.Find(hero.Id)!.HasChildren);
    });

    [Fact]
    public void CollapsingAndExpandingAgainShowsEachDescendantOnce() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var tree = editor.Get<HierarchyViewModel>().Tree;
        var group = model.CreateEntity("Group", siblingIndex: 0);
        var first = model.CreateEntity("First", group.Id);
        model.CreateEntity("Nested", first.Id);
        model.CreateEntity("Second", group.Id);
        tree.SetExpanded(first.Id, true);
        tree.SetExpanded(group.Id, true);
        var expanded = Names(tree);
        List<string> collapsed = [expanded[0], .. expanded.Skip(4)];
        Assert.Equal(["Group", "First", "Nested", "Second"], expanded.Take(4));

        for (var i = 0; i < 3; i++)
        {
            tree.SetExpanded(group.Id, false);
            Assert.Equal(collapsed, Names(tree));
            tree.SetExpanded(group.Id, true);
            Assert.Equal(expanded, Names(tree));
        }

        tree.SetExpandedRecursive(group.Id, false);
        Assert.Equal(collapsed, Names(tree));
        tree.SetExpandedRecursive(group.Id, true);
        Assert.Equal(expanded, Names(tree));

        tree.Rebuild();
        Assert.Equal(expanded, Names(tree));
    });

    [Fact]
    public void DroppedTextureAssetsBecomeSpritesInOneUndoStep() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var hierarchy = editor.Get<HierarchyViewModel>();
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var texture = Talesmith.Editor.Inspector.JsonValues.Asset(model.GetProperty(hero.Id, "Sprite", "texture"))!.Value;
        var count = model.Entities.Count;

        Assert.True(hierarchy.TryReadDrag(EditorDragData.ForAssets([texture]), out var data));
        var asset = Assert.Single(data.Assets);
        Assert.EndsWith(".png", asset.Path, StringComparison.Ordinal);
        Assert.True(hierarchy.Drop(data, hierarchy.Tree.Find(hero.Id), DropPosition.Inside));

        var created = Assert.Single(editor.Get<ISelectionService>().Entities);
        Assert.Equal(hero.Id, model.Get(created).Parent);
        Assert.Equal(texture.ToString(), model.GetProperty(created, "Sprite", "texture")!.GetValue<string>());
        editor.Get<IUndoService>().Undo();
        Assert.Equal(count, model.Entities.Count);
    });

    private static EditorDragData Read(HierarchyViewModel hierarchy, IReadOnlyList<Guid> entities)
    {
        Assert.True(hierarchy.TryReadDrag(EditorDragData.ForEntities(entities), out var data));
        return data;
    }

    private static List<string> Names(HierarchyTree tree) => [.. tree.Rows.Select(r => r.Name)];
}
