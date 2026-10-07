using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;

namespace Talesmith.Editor.Tests.PlayMode;

public sealed class LiveEditTests
{
    [Fact]
    public void PlayEditsChangeThePlayWorldButNotTheDocument() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var undo = editor.Get<IUndoService>();
        var hero = model.Entities.Single(e => e.Name == "Hero");
        editor.Get<ISelectionService>().SelectEntity(hero.Id);
        var before = DocumentSerializer.Write(model.Document);
        var steps = undo.UndoSteps.Count;
        var play = editor.Get<IPlayModeService>();

        await play.PlayAsync();
        for (var i = 0; i < 600 && !await play.InvokeAsync(g => g.Scenes.Current is DocumentScene && !g.Scenes.IsLoading); i++)
            await Task.Delay(5);

        var live = editor.Get<LiveSelection>();
        await live.SyncFromSelectionAsync();
        Assert.False(live.Entity.IsNull);

        var rows = await play.InvokeAsync(LiveHierarchy.Capture);
        Assert.Contains(rows, r => r.Key == hero.Id && r.Name == "Hero");

        var entity = live.Entity;
        var snapshot = await play.InvokeAsync(g => LiveInspectorData.Capture(g, entity));
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot.Components, c => c.Type == "Transform");
        var data = new LiveInspectorData(play, entity, snapshot);
        var changed = 0;
        var value = data.CreateValue("Transform", "position", new PropertyDescriptor("position", "Position", PropertyKind.Vector2, typeof(Vector2)));
        value.Changed += (_, _) => changed++;
        value.Set(new JsonArray(777, 333));
        Assert.Equal(1, changed);

        var position = await play.InvokeAsync(g => g.Scenes.Current!.World.Get<Transform>(entity).Position);
        Assert.Equal(new Vector2(777, 333), position);
        Assert.Equal(before, DocumentSerializer.Write(model.Document));
        Assert.Equal(steps, undo.UndoSteps.Count);

        await play.StopAsync();
        Assert.Equal(before, DocumentSerializer.Write(model.Document));
        Assert.True(live.Entity.IsNull);
        await Assert.ThrowsAsync<InvalidOperationException>(() => play.InvokeAsync(_ => 1));
        Assert.Equal(-1, await play.TryInvokeAsync(_ => 1, -1));
    });

    [Fact]
    public void TheLiveHierarchyNamesIconsWithoutCreatingThemOnTheGameThread() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var play = editor.Get<IPlayModeService>();
        await play.PlayAsync();
        for (var i = 0; i < 600 && !await play.InvokeAsync(g => g.Scenes.Current is DocumentScene && !g.Scenes.IsLoading); i++)
            await Task.Delay(5);

        var rows = await play.InvokeAsync(LiveHierarchy.Capture);

        var icons = rows.Select(r => r.Icon).OfType<string>().Distinct().ToList();
        Assert.NotEmpty(icons);
        Assert.All(icons, name => Assert.True(Icons.Find(name)!.CheckAccess(), $"The {name} icon belongs to the game thread, so the UI thread cannot draw it."));
        await play.StopAsync();
    });
}
