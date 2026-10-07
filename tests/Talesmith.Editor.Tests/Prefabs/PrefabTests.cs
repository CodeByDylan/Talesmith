using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Undo;

namespace Talesmith.Editor.Tests.Prefabs;

public sealed class PrefabTests
{
    [Fact]
    public void CreatingOverridingRevertingAndApplyingAPrefab() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var workflow = editor.Get<PrefabWorkflow>();
        var entities = editor.Get<EntityDataService>();
        var library = editor.Get<PrefabLibrary>();
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var layer = JsonValues.Number(model.GetProperty(hero.Id, "Sprite", "layer"));

        var guid = await workflow.CreatePrefabAsync(hero.Id);
        Assert.NotNull(guid);
        Assert.True(editor.Get<IProjectService>().Catalog.TryGetPath(guid.Value, out var path));
        Assert.StartsWith("prefabs/", path, StringComparison.Ordinal);
        Assert.EndsWith(".tprefab", path, StringComparison.Ordinal);
        var instance = model.Get(hero.Id);
        Assert.Equal(guid, instance.Prefab?.Asset);
        Assert.DoesNotContain(instance.Components, c => c.Type == "Sprite");
        Assert.NotNull(entities.GetComponent(hero.Id, "Sprite"));
        Assert.False(entities.IsOverridden(hero.Id, "Sprite", "layer"));

        entities.Set(hero.Id, "Sprite", "layer", 500);
        Assert.Equal(500, JsonValues.Number(entities.Get(hero.Id, "Sprite", "layer")));
        Assert.True(entities.IsOverridden(hero.Id, "Sprite", "layer"));
        Assert.Single(model.Get(hero.Id).Prefab!.Overrides);

        workflow.Revert(hero.Id);
        Assert.Equal(layer, JsonValues.Number(entities.Get(hero.Id, "Sprite", "layer")));
        Assert.Empty(model.Get(hero.Id).Prefab!.Overrides);
        editor.Get<IUndoService>().Undo();
        Assert.True(entities.IsOverridden(hero.Id, "Sprite", "layer"));

        await workflow.ApplyAsync(hero.Id);
        Assert.Empty(model.Get(hero.Id).Prefab!.Overrides);
        Assert.Equal(500, JsonValues.Number(entities.Get(hero.Id, "Sprite", "layer")));
        Assert.Equal(500, JsonValues.Number(library.Get(guid.Value)!.Root!.FindComponent("Sprite")!.Data["layer"]));

        var second = workflow.Instantiate(guid.Value, null, new Vector2(64, 0))!.Value;
        Assert.Equal(500, JsonValues.Number(entities.Get(second, "Sprite", "layer")));
        Assert.Equal(new Vector2(64, 0), JsonValues.Vector(model.GetProperty(second, "Transform", "position")));
    });

    [Fact]
    public void EditingAPrefabMemberRecordsAnOverrideOnItsInstance() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var workflow = editor.Get<PrefabWorkflow>();
        var entities = editor.Get<EntityDataService>();
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var hat = model.CreateEntity("Hat", hero.Id, [new Runtime.Serialization.ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, -8) })]);

        await workflow.CreatePrefabAsync(hero.Id);
        Assert.False(model.Contains(hat.Id));
        var member = editor.Get<PrefabInstances>().GetMembers(hero.Id).Single(m => m.Entity.Name == "Hat");
        Assert.True(entities.Exists(member.Id));

        entities.Set(member.Id, "Transform", "position", new JsonArray(4, -12));
        Assert.Equal(new Vector2(4, -12), JsonValues.Vector(entities.Get(member.Id, "Transform", "position")));
        var change = Assert.Single(model.Get(hero.Id).Prefab!.Overrides);
        Assert.Equal("Transform", change.Component);
        Assert.Equal("position", change.Path);

        entities.RevertToPrefab(member.Id, "Transform", "position");
        Assert.Equal(new Vector2(0, -8), JsonValues.Vector(entities.Get(member.Id, "Transform", "position")));
    });
}
