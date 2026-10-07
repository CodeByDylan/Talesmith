using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Tests.Viewport;

public sealed class EditWorldTests
{
    [Fact]
    public void TheOpenSceneLoadsOnceAfterTheEditGamesOwnStartScene() => Headless.Run(async () =>
    {
        var loaded = new List<string>();
        IDisposable? subscription = null;
        await using var editor = await EditorFixture.OpenAsync(beforeStart: services =>
        {
            var project = services.GetRequiredService<IProjectService>();
            project.StatusChanged += (_, _) => subscription ??= project.EditSession?.Game.Services.GetRequiredService<IEventBus>()
                .Subscribe((ref SceneLoaded e) => loaded.Add(e.Scene.GetType().Name));
        });

        Assert.Equal([nameof(EmptyScene), nameof(DocumentScene)], loaded);
        Assert.False(editor.World.IsBusy);
        subscription?.Dispose();
    });

    [Fact]
    public void TheOpenSceneLoadsWhenNoViewRunsTheEditGame() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(runFrames: false);

        Assert.NotNull(editor.World.World);
        Assert.False(editor.World.IsBusy);
        Assert.True(editor.World.TryGetEntity(editor.Document.Entities.Single(e => e.Name == "Hero").Id, out _));
    });

    [Fact]
    public void TheWorldMirrorsTheDocumentAndFollowsItsChanges() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var world = editor.World;
        var hero = model.Entities.Single(e => e.Name == "Hero");
        Assert.True(world.TryGetEntity(hero.Id, out var heroEntity));
        Assert.True(world.World!.Has<Sprite>(heroEntity));

        model.SetProperty(hero.Id, "Transform", "position", new JsonArray(300, 40));
        await world.WhenIdle;
        Assert.Equal(new Vector2(300, 40), world.World.Get<Transform>(heroEntity).Position);

        model.Rename(hero.Id, "Heroine");
        Assert.Equal("Heroine", world.World.Get<Name>(heroEntity).Value);

        model.SetHidden(hero.Id, true);
        Assert.True(world.World.Has<Inactive>(heroEntity));
        model.SetHidden(hero.Id, false);
        Assert.False(world.World.Has<Inactive>(heroEntity));

        model.RemoveComponent(hero.Id, "Sprite");
        Assert.False(world.World.Has<Sprite>(heroEntity));
        editor.Get<IUndoService>().Undo();
        await world.WhenIdle;
        Assert.True(world.World.Has<Sprite>(heroEntity));
    });

    [Fact]
    public void CreatedMovedAndDeletedEntitiesAppearMoveAndDisappear() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var world = editor.World;
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var heroSprite = hero.FindComponent("Sprite")!.Clone();
        var count = world.EntityCount;

        var crate = model.CreateEntity("Crate", null, [new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(64, 0) }), heroSprite]);
        await world.WhenIdle;
        Assert.True(world.TryGetEntity(crate.Id, out var crateEntity));
        Assert.Equal(count + 1, world.EntityCount);
        Assert.False(world.World!.Get<Sprite>(crateEntity).Texture.IsNone);

        model.MoveEntity(crate.Id, hero.Id);
        await world.WhenIdle;
        Assert.True(world.TryGetEntity(hero.Id, out var heroEntity));
        Assert.Equal(heroEntity, world.World.Get<Parent>(crateEntity).Value);
        Assert.True(world.TryGetDocumentId(crateEntity, out var id));
        Assert.Equal(crate.Id, id);

        model.DeleteEntities([hero.Id]);
        await world.WhenIdle;
        Assert.False(world.World.IsAlive(crateEntity));
        Assert.False(world.World.IsAlive(heroEntity));

        editor.Get<IUndoService>().Undo();
        await world.WhenIdle;
        Assert.True(world.TryGetEntity(hero.Id, out _));
        Assert.True(world.TryGetEntity(crate.Id, out var restored));
        Assert.True(world.World.Has<Parent>(restored));
    });

    [Fact]
    public void TheWorldFollowsAnEditGameThatWasReplacedBeforeItsNextFrame() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var hero = editor.Document.Entities.Single(e => e.Name == "Hero");

        await project.ReplaceEditSessionAsync(null);
        project.EditSession!.Game.Tick(1.0 / 60);
        await project.ReplaceEditSessionAsync(null);
        var latest = project.EditSession!.Game;
        await editor.WaitAsync(() => ReferenceEquals(editor.World.Game, latest) && editor.World.World is not null && !editor.World.IsBusy);

        Assert.True(editor.World.TryGetEntity(hero.Id, out _));
    });

    [Fact]
    public void TheWorldFollowsAnEditGameThatWasReplacedWhileItLoadedTheScene() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var hero = editor.Document.Entities.Single(e => e.Name == "Hero");

        await project.ReplaceEditSessionAsync(null);
        project.EditSession!.Game.Tick(1.0 / 60);
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.World.IsBusy);
        await project.ReplaceEditSessionAsync(null);
        var latest = project.EditSession!.Game;
        await editor.WaitAsync(() => ReferenceEquals(editor.World.Game, latest) && editor.World.World is not null && !editor.World.IsBusy);

        Assert.True(editor.World.TryGetEntity(hero.Id, out _));
    });

    [Fact]
    public void ThePickerFindsEntitiesUnderAPoint() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var hero = editor.Document.Entities.Single(e => e.Name == "Hero");
        var picker = editor.Get<EntityPicker>();
        var position = JsonFormats.ReadVector2(hero.FindComponent("Transform")!.Data["position"]!);

        Assert.Equal(hero.Id, picker.HitTest(position + new Vector2(0, -8), 1));
        var area = picker.Query(new Mathematics.Rect2(position.X - 40, position.Y - 40, 80, 80), 1);
        Assert.Contains(hero.Id, area);
        Assert.NotNull(picker.GetBounds([hero.Id], 1));
    });

    [Fact]
    public void ThePickerCollectsAgainOnlyAfterTheWorldOrZoomChanged() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var hero = model.Entities.Single(e => e.Name == "Hero");
        var picker = editor.Get<EntityPicker>();
        var visuals = picker.Collect(1);

        Assert.Same(visuals, picker.Collect(1));
        Assert.NotSame(visuals, picker.Collect(2));

        model.SetProperty(hero.Id, "Transform", "position", new JsonArray(900, 700));
        await editor.World.WhenIdle;
        Assert.Equal(new Vector2(900, 700), picker.Collect(2).Single(v => v.DocumentId == hero.Id).Position);
    });
}
