using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Tests.Scenes;

public sealed class DocumentSceneTests : IAsyncDisposable
{
    private readonly MemoryAssetSource _files = new();
    private readonly FakeCatalog _catalog = new();
    private readonly Game _game;
    private readonly AssetGuid _sceneGuid;
    private readonly SceneDocument _document;

    public DocumentSceneTests()
    {
        var map = _catalog.Add("maps/island.testmap");
        _files.Write("maps/island.testmap", "");
        _document = SceneDocument.Create();
        _document.Environment.ClearColor = new Color(0x10, 0x20, 0x30);
        _document.Environment.Gravity = new Vector2(0, 500);
        _document.Entities.Add(new EntityDocument
        {
            Id = Guid.NewGuid(),
            Name = "Island",
            Components =
            [
                new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(10, 20) }),
                new ComponentDocument("TileMapRenderer", new JsonObject { ["map"] = map.ToString() })
            ]
        });
        _document.Entities.Add(new EntityDocument { Id = Guid.NewGuid(), Name = "Camera", Components = [new ComponentDocument("Camera", [])] });
        _files.Write("scenes/main.tscene", DocumentSerializer.Write(_document));
        _sceneGuid = _catalog.Add("scenes/main.tscene");

        var builder = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings());
        builder.Services.AddSingleton<IAssetSource>(_files);
        builder.Services.AddSingleton<IAssetCatalog>(_catalog);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, TestMapImporter>());
        _game = builder.Build();
    }

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task LoadsASceneFileByGuidAndAppliesItsEnvironment()
    {
        var maps = new List<MapLoaded>();
        var created = 0;
        var events = _game.Services.GetRequiredService<IEventBus>();
        using var mapSubscription = events.Subscribe((ref MapLoaded e) => maps.Add(e));
        using var createdSubscription = events.Subscribe((ref EntityCreated _) => created++);

        await _game.Scenes.LoadAsync(DocumentScene.ForGuid(_sceneGuid), SceneTransition.Instant, TestContext.Current.CancellationToken);

        var scene = Assert.IsType<DocumentScene>(_game.Scenes.Current);
        Assert.Equal(new Color(0x10, 0x20, 0x30), scene.ClearColor);
        Assert.Equal(new Vector2(0, 500), scene.Environment.Gravity);
        Assert.Equal(_document.Id, scene.Document!.Id);
        var island = scene.Entities!.Entities[_document.Entities[0].Id];
        Assert.Equal("maps/island.testmap", Assert.Single(maps).Map.Path);
        Assert.Equal(island, maps[0].MapEntity);
        Assert.Equal(3, scene.World.EntityCount);
        Assert.True(created >= 3);
    }

    [Fact]
    public async Task LoadsASceneFileByPath()
    {
        await _game.Scenes.LoadAsync(DocumentScene.ForPath("scenes/main.tscene"), SceneTransition.Instant, TestContext.Current.CancellationToken);

        var scene = Assert.IsType<DocumentScene>(_game.Scenes.Current);
        Assert.True(scene.World.Query<Camera>().TryGetSingle(out _));
    }

    [Fact]
    public async Task RunsDocumentsHeldInMemory()
    {
        var memory = _game.Services.GetRequiredService<InMemorySceneDocuments>();
        var unsaved = SceneDocument.Create();
        unsaved.Entities.Add(new EntityDocument { Id = Guid.NewGuid(), Name = "Unsaved" });
        var edited = _document.Clone();
        edited.Entities.RemoveAt(0);

        await _game.Scenes.LoadAsync(memory.Add(unsaved), SceneTransition.Instant, TestContext.Current.CancellationToken);
        Assert.Equal("Unsaved", Single(_game.Scenes.Current!.World));

        memory.Override("scenes/main.tscene", edited);
        await _game.Scenes.LoadAsync(DocumentScene.ForGuid(_sceneGuid), SceneTransition.Instant, TestContext.Current.CancellationToken);
        Assert.Equal("Camera", Single(_game.Scenes.Current!.World));
    }

    [Fact]
    public async Task NeedsAPathOrGuid()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _game.Scenes.LoadAsync(new SceneRequest(DocumentScene.SceneName), SceneTransition.Instant, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PausingRaisesAnEvent()
    {
        var states = new List<bool>();
        using var subscription = _game.Services.GetRequiredService<IEventBus>().Subscribe((ref PauseStateChanged e) => states.Add(e.IsPaused));

        _game.IsPaused = true;
        _game.IsPaused = true;
        _game.IsPaused = false;

        Assert.Equal([true, false], states);
    }

    [Fact]
    public void StartScenesCanNameASceneFile()
    {
        var folder = Directory.CreateTempSubdirectory("talesmith-settings-");
        try
        {
            Directory.CreateDirectory(Path.Combine(folder.FullName, "config"));
            File.WriteAllText(Path.Combine(folder.FullName, GameSettings.FileName), """{ "startScene": "scenes/main.tscene" }""");
            var fromPath = GameSettings.Load(folder.FullName).StartScene;
            File.WriteAllText(Path.Combine(folder.FullName, GameSettings.FileName), """{ "startScene": { "name": "map", "parameters": { "map": "a.hexy", "zoom": 2 } } }""");
            var fromObject = GameSettings.Load(folder.FullName).StartScene;

            Assert.Equal(DocumentScene.SceneName, fromPath.Name);
            Assert.Equal("scenes/main.tscene", fromPath.Get(DocumentScene.PathParameter));
            Assert.Equal("map", fromObject.Name);
            Assert.Equal("2", fromObject.Get("zoom"));
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private static string Single(World world)
    {
        Assert.True(world.Query<Name>().TryGetSingle(out var entity));
        return world.Get<Name>(entity).Value;
    }
}
