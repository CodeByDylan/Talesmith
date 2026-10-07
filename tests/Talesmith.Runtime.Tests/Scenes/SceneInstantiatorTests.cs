using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Authoring;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Systems;
using Talesmith.Systems;

namespace Talesmith.Runtime.Tests.Scenes;

public sealed class SceneInstantiatorTests : IDisposable
{
    private readonly SceneTestHost _host = new(services => services.AddComponent<Probe>());
    private readonly AssetGuid _sheet;
    private readonly AssetGuid _map;

    public SceneInstantiatorTests()
    {
        _sheet = _host.AddFile("sprites/hero.sheet");
        _map = _host.AddFile("maps/island.testmap");
    }

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EveryKindOfValueRoundTripsThroughApplyAndCapture()
    {
        var target = Guid.NewGuid();
        var data = JsonNode.Parse($$"""
            {
              "flag": true, "small": 7, "number": -12, "big": 9000000000, "huge": 5, "ratio": 0.25, "precise": 0.1, "money": 12.5,
              "text": "hello", "key": "{{Guid.NewGuid():N}}", "kind": "second", "flags": "a, c",
              "point": [1.5, -2], "tint": "#80FF0000", "area": [1, 2, 3, 4],
              "curve": [{ "time": 0, "value": 1, "outTangent": 0.5, "interpolation": "linear" }],
              "gradient": [{ "position": 0, "color": "#000000" }, { "position": 1, "color": "#FFFFFF" }],
              "maybe": null, "maybeArea": [5, 6, 7, 8],
              "samples": [0.5, 1], "names": ["x", "y"], "path": [[1, 2], [3, 4]], "steps": [1, 2, 3], "set": ["a", "b"],
              "nested": { "count": 3, "label": "n" }, "inner": { "offset": [1, 1], "color": "#00FF00" },
              "reference": "{{AssetGuid.NewGuid()}}", "other": "{{target:N}}",
              "texture": "{{_sheet}}", "sheet": "{{_sheet}}", "map": "{{_map}}"
            }
            """)!.AsObject();
        var scene = Scene(Entity("Probe holder", new ComponentDocument("Probe", data)), new EntityDocument { Id = target, Name = "Target" });
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, scene, cancellationToken: TestContext.Current.CancellationToken);
        var captured = _host.Capture.CaptureEntity(world, result.Roots[0]).FindComponent("Probe")!.Data;

        Assert.True(JsonNode.DeepEquals(data, captured), captured.ToJsonString());
        var probe = world.Get<Probe>(result.Roots[0]);
        Assert.Equal(result.Entities[target], probe.Other);
        Assert.Equal(Options.A | Options.C, probe.Flags);
        Assert.False(probe.Texture.IsNone);
        Assert.Equal("sprites/hero.sheet", probe.Sheet!.Path);
        Assert.Equal("maps/island.testmap", probe.Map!.Path);
    }

    [Fact]
    public void DefaultsOfEveryBuiltInComponentRoundTrip()
    {
        var world = new World();
        var context = new StubContext();
        foreach (var definition in _host.Components.Definitions)
        {
            var entity = world.Create();
            var defaults = definition.CreateDefault();

            definition.Apply(world, entity, defaults, context);

            var captured = definition.Capture(world, entity, context);
            Assert.True(JsonNode.DeepEquals(defaults, captured), $"{definition.TypeName}: {defaults.ToJsonString()} became {captured?.ToJsonString()}");
            definition.Remove(world, entity);
            Assert.Null(definition.Capture(world, entity, context));
        }
    }

    [Fact]
    public async Task EntitiesGetIdsNamesHierarchyAndActivity()
    {
        var root = Entity("Root", Transform(100, 0));
        var child = Entity("Child", Transform(10, 0));
        child.Parent = root.Id;
        child.Active = false;
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(root, child), cancellationToken: TestContext.Current.CancellationToken);

        var entity = result.Entities[child.Id];
        Assert.Equal([result.Entities[root.Id]], result.Roots);
        Assert.Equal(child.Id, world.Get<SceneEntityId>(entity).Value);
        Assert.Equal("Child", world.Get<Name>(entity).Value);
        Assert.True(world.Has<Inactive>(entity));
        Assert.Equal(result.Entities[root.Id], world.Get<Parent>(entity).Value);
        Assert.Equal(new Vector2(10, 0), world.Get<LocalTransform>(entity).Position);
        Assert.Equal(new Vector2(110, 0), world.Get<Transform>(entity).Position);

        var captured = _host.Capture.Capture(world);
        Assert.Equal([root.Id, child.Id], captured.Entities.Select(e => e.Id));
        Assert.Equal(root.Id, captured.Entities[1].Parent);
        Assert.False(captured.Entities[1].Active);
        Assert.Equal("[10,0]", captured.Entities[1].FindComponent("Transform")!.Data["position"]!.ToJsonString());
    }

    [Fact]
    public async Task SpritesFollowTheirNamedSpriteUnlessSet()
    {
        var following = Entity("Following", new ComponentDocument("Sprite", new JsonObject { ["texture"] = _sheet.ToString(), ["sprite"] = "right" }));
        var sized = Entity("Sized", new ComponentDocument("Sprite", new JsonObject { ["texture"] = _sheet.ToString(), ["sprite"] = "right", ["size"] = new JsonArray(10, 10) }));
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(following, sized), cancellationToken: TestContext.Current.CancellationToken);

        var sprite = world.Get<Sprite>(result.Entities[following.Id]);
        Assert.Equal(new Rect2(32, 0, 32, 32), sprite.Source);
        Assert.Equal(new Vector2(32, 32), sprite.Size);
        Assert.Equal(new Vector2(0.25f, 0.75f), sprite.Origin);
        Assert.Equal(new Vector2(10, 10), world.Get<Sprite>(result.Entities[sized.Id]).Size);

        var capturedFollowing = _host.Capture.CaptureEntity(world, result.Entities[following.Id]).FindComponent("Sprite")!.Data;
        var capturedSized = _host.Capture.CaptureEntity(world, result.Entities[sized.Id]).FindComponent("Sprite")!.Data;
        Assert.Equal(_sheet.ToString(), capturedFollowing["texture"]!.GetValue<string>());
        Assert.False(capturedFollowing.ContainsKey("size"));
        Assert.False(capturedFollowing.ContainsKey("source"));
        Assert.Equal("[10,10]", capturedSized["size"]!.ToJsonString());
    }

    [Fact]
    public async Task PrefabInstancesGetDerivedIdsOverridesAndTheirOwnReferences()
    {
        var root = Entity("Turret", Transform(0, 0), new ComponentDocument("Sprite", new JsonObject { ["tint"] = "#FFFFFF" }));
        var barrel = Entity("Barrel", Transform(5, 0));
        barrel.Parent = root.Id;
        root.Components.Add(new ComponentDocument("Camera", new JsonObject { ["target"] = barrel.Id.ToString("N") }));
        var prefab = Prefab(root, barrel);
        var guid = _host.AddPrefab("prefabs/turret.tprefab", prefab);

        var first = Instance(guid, "First", Transform(100, 0));
        first.Prefab!.Overrides.Add(new PrefabOverride { Entity = barrel.Id, Component = "Transform", Path = "position", Value = new JsonArray(7, 0) });
        first.Prefab.Overrides.Add(new PrefabOverride { Entity = root.Id, Component = "Sprite", Path = "tint", Value = "#FF0000" });
        var second = Instance(guid, "", Transform(200, 0));
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(first, second), cancellationToken: TestContext.Current.CancellationToken);

        var firstRoot = result.Entities[first.Id];
        var firstBarrel = result.Entities[PrefabIds.Derive(first.Id, barrel.Id)];
        var secondRoot = result.Entities[second.Id];
        var secondBarrel = result.Entities[PrefabIds.Derive(second.Id, barrel.Id)];
        Assert.Equal(4, result.Entities.Count);
        Assert.Equal([firstRoot, secondRoot], result.Roots);
        Assert.Equal(guid, world.Get<PrefabInstance>(firstRoot).Prefab);
        Assert.Equal("First", world.Get<Name>(firstRoot).Value);
        Assert.Equal("Turret", world.Get<Name>(secondRoot).Value);
        Assert.Equal(new Vector2(107, 0), world.Get<Transform>(firstBarrel).Position);
        Assert.Equal(new Vector2(205, 0), world.Get<Transform>(secondBarrel).Position);
        Assert.Equal(new Color(255, 0, 0), world.Get<Sprite>(firstRoot).Tint);
        Assert.Equal(Color.White, world.Get<Sprite>(secondRoot).Tint);
        Assert.Equal(firstBarrel, world.Get<Camera>(firstRoot).Target);
        Assert.Equal(secondBarrel, world.Get<Camera>(secondRoot).Target);
        Assert.Equal(new Vector2(100, 0), world.Get<Camera>(firstRoot).View.Position);
        Assert.Equal("#FFFFFF", prefab.Entities[0].FindComponent("Sprite")!.Data["tint"]!.GetValue<string>());
    }

    [Fact]
    public async Task NestedPrefabsExpandAndTakeOverridesFromEveryLevel()
    {
        var innerRoot = Entity("Inner", new ComponentDocument("Tags", new JsonObject { ["values"] = new JsonArray("inner") }));
        var innerChild = Entity("Inner child", Transform(1, 0));
        innerChild.Parent = innerRoot.Id;
        var inner = _host.AddPrefab("prefabs/inner.tprefab", Prefab(innerRoot, innerChild));

        var outerRoot = Entity("Outer", Transform(0, 0));
        var nested = Instance(inner, "Nested", Transform(0, 0));
        nested.Parent = outerRoot.Id;
        nested.Prefab!.Overrides.Add(new PrefabOverride { Entity = innerChild.Id, Component = "Transform", Path = "position", Value = new JsonArray(2, 0) });
        var outer = _host.AddPrefab("prefabs/outer.tprefab", Prefab(outerRoot, nested));

        var overridden = Instance(outer, "Overridden", Transform(0, 0));
        overridden.Prefab!.Overrides.Add(new PrefabOverride
        {
            Entity = PrefabIds.Derive(nested.Id, innerChild.Id),
            Component = "Transform",
            Path = "position",
            Value = new JsonArray(3, 0)
        });
        var plain = Instance(outer, "Plain", Transform(0, 0));
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(overridden, plain), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, result.Entities.Count);
        var nestedRoot = result.Entities[PrefabIds.Derive(overridden.Id, nested.Id)];
        Assert.Equal(inner, world.Get<PrefabInstance>(nestedRoot).Prefab);
        Assert.True(world.Get<Tags>(nestedRoot).Has("inner"));
        Assert.Equal("Nested", world.Get<Name>(nestedRoot).Value);
        Assert.Equal(result.Entities[overridden.Id], world.Get<Parent>(nestedRoot).Value);
        var overriddenChild = result.Entities[PrefabIds.Derive(overridden.Id, PrefabIds.Derive(nested.Id, innerChild.Id))];
        var plainChild = result.Entities[PrefabIds.Derive(plain.Id, PrefabIds.Derive(nested.Id, innerChild.Id))];
        Assert.Equal(new Vector2(3, 0), world.Get<LocalTransform>(overriddenChild).Position);
        Assert.Equal(new Vector2(2, 0), world.Get<LocalTransform>(plainChild).Position);
    }

    [Fact]
    public async Task InstancesCanRemoveAndAddComponents()
    {
        var root = Entity("Thing", Transform(0, 0), new ComponentDocument("Tags", new JsonObject { ["values"] = new JsonArray("a") }));
        var guid = _host.AddPrefab("prefabs/thing.tprefab", Prefab(root));
        var instance = Instance(guid, "", new ComponentDocument("Camera", new JsonObject()));
        instance.Prefab!.RemovedComponents.Add(new PrefabComponentRef { Entity = root.Id, Component = "Tags" });
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(instance), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(world.Has<Tags>(result.Root));
        Assert.True(world.Has<Camera>(result.Root));
        Assert.True(world.Has<Transform>(result.Root));
    }

    [Fact]
    public async Task MissingComponentsAssetsAndPrefabsAreLoggedAndSkipped()
    {
        var unknown = Entity("Unknown", new ComponentDocument("Plugin.Missing", new JsonObject { ["value"] = 1 }), Transform(1, 2));
        var missingTexture = Entity("Missing texture", new ComponentDocument("Sprite", new JsonObject { ["texture"] = AssetGuid.NewGuid().ToString() }));
        var missingPrefab = Instance(AssetGuid.NewGuid(), "Missing prefab", Transform(3, 4));
        var badValue = Entity("Bad value", new ComponentDocument("Transform", new JsonObject { ["position"] = "not a vector", ["rotation"] = 1 }));
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(unknown, missingTexture, missingPrefab, badValue), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Entities.Count);
        Assert.Equal(new Vector2(1, 2), world.Get<Transform>(result.Entities[unknown.Id]).Position);
        Assert.True(world.Get<Sprite>(result.Entities[missingTexture.Id]).Texture.IsNone);
        Assert.Equal(new Vector2(3, 4), world.Get<Transform>(result.Entities[missingPrefab.Id]).Position);
        Assert.Equal(1, world.Get<Transform>(result.Entities[badValue.Id]).Rotation);
        Assert.True(_host.Logs.Contains(LogLevel.Warning, "Plugin.Missing"));
        Assert.True(_host.Logs.Contains(LogLevel.Warning, "is not in the asset catalog"));
        Assert.True(_host.Logs.Contains(LogLevel.Warning, "could not be loaded, so only the instance entity is created"));
        Assert.True(_host.Logs.Contains(LogLevel.Warning, "position"));
    }

    [Fact]
    public async Task PrefabsThatContainThemselvesStop()
    {
        var root = Entity("Loop", Transform(0, 0));
        var guid = _host.Catalog.Add("prefabs/loop.tprefab");
        var self = Instance(guid, "Self", Transform(0, 0));
        self.Parent = root.Id;
        _host.Files.Write("prefabs/loop.tprefab", DocumentSerializer.Write(Prefab(root, self)));
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(Instance(guid, "Top", Transform(0, 0))), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Entities.Count);
        Assert.True(_host.Logs.Contains(LogLevel.Error, "contains an instance of itself"));
    }

    [Fact]
    public async Task TileMapRenderersShowTheirMapAndSpawnItsObjectsAsChildren()
    {
        var map = Entity("Map", Transform(1000, 0), new ComponentDocument("TileMapRenderer", new JsonObject { ["map"] = _map.ToString(), ["renderLayer"] = 120 }));
        var world = new World();

        var result = await _host.Instantiator.InstantiateAsync(world, Scene(map), cancellationToken: TestContext.Current.CancellationToken);

        var entity = result.Entities[map.Id];
        Assert.Equal(120, world.Get<TileMapComponent>(entity).RenderLayer);
        Assert.Equal(1, world.Query<MapObjectComponent>().Count);
        Assert.True(world.Query<MapObjectComponent>().TryGetSingle(out var spawned));
        Assert.Equal(entity, world.Get<Parent>(spawned).Value);
        Assert.Equal(new Vector2(1128, 192), world.Get<Transform>(spawned).Position);
        Assert.Single(_host.Capture.Capture(world).Entities);

        var definition = _host.Components.Find("TileMapRenderer")!;
        var data = new JsonObject { ["map"] = _map.ToString() };
        data["renderLayer"] = 130;
        definition.Apply(world, entity, data, new StubContext(_host.Services.GetRequiredService<IAssetManager>().Load<TileMap>("maps/island.testmap")));
        Assert.True(world.IsAlive(spawned));
        Assert.Equal(130, world.Get<TileMapComponent>(entity).RenderLayer);

        definition.Remove(world, entity);
        Assert.False(world.IsAlive(spawned));
        Assert.False(world.Has<TileMapComponent>(entity));
    }

    [Fact]
    public async Task SpriteAnimatorsPlayTheTexturesAnimation()
    {
        var animated = Entity("Walker", Transform(0, 0), new ComponentDocument("SpriteAnimator", new JsonObject { ["texture"] = _sheet.ToString(), ["animation"] = "walk" }));
        var world = new World();
        var result = await _host.Instantiator.InstantiateAsync(world, Scene(animated), cancellationToken: TestContext.Current.CancellationToken);
        var entity = result.Entities[animated.Id];
        var system = new SpriteAnimatorSystem(_host.Services.GetRequiredService<TextureCache>());
        var commands = new CommandBuffer(world);

        system.Update(new SystemContext(world, default, commands));
        commands.Playback();

        var animation = world.Get<SpriteAnimation>(entity);
        Assert.Equal("walk", animation.Clip.Name);
        Assert.Equal(2, animation.Clip.Frames.Count);
        Assert.True(animation.Playing);
        Assert.Equal(new Rect2(0, 0, 32, 32), world.Get<Sprite>(entity).Source);

        world.Get<SpriteAnimator>(entity).Playing = false;
        system.Update(new SystemContext(world, default, commands));
        Assert.False(world.Get<SpriteAnimation>(entity).Playing);
    }

    [Fact]
    public async Task ThePrefabServiceSpawnsAtAPosition()
    {
        var root = Entity("Coin", Transform(0, 0));
        var child = Entity("Sparkle", Transform(0, -8));
        child.Parent = root.Id;
        var guid = _host.AddPrefab("prefabs/coin.tprefab", Prefab(root, child));
        var world = new World();
        var service = new PrefabService(_host.Instantiator, new NoScenes(), _host.Services.GetRequiredService<IAssetManager>(), _host.Catalog,
            _host.Services.GetRequiredService<AssetReferences>());

        var byGuid = await service.InstantiateAsync(guid, new PrefabSpawn { World = world, Position = new Vector2(50, 50) }, cancellationToken: TestContext.Current.CancellationToken);
        var byPath = await service.InstantiateAsync("prefabs/coin.tprefab", new PrefabSpawn { World = world, Position = new Vector2(-50, 0), Parent = byGuid.Root }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new Vector2(50, 50), world.Get<Transform>(byGuid.Root).Position);
        Assert.Equal(new Vector2(50, 42), world.Get<Transform>(byGuid.Entities[PrefabIds.Derive(world.Get<SceneEntityId>(byGuid.Root).Value, child.Id)]).Position);
        Assert.Equal(byGuid.Root, world.Get<Parent>(byPath.Root).Value);
        Assert.Equal(new Vector2(0, 50), world.Get<Transform>(byPath.Root).Position);
        Assert.Equal(guid, world.Get<PrefabInstance>(byPath.Root).Prefab);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstantiateAsync(guid, cancellationToken: TestContext.Current.CancellationToken));
    }

    private static SceneDocument Scene(params EntityDocument[] entities)
    {
        var scene = SceneDocument.Create();
        scene.Entities.AddRange(entities);
        return scene;
    }

    private static PrefabDocument Prefab(params EntityDocument[] entities)
    {
        var prefab = PrefabDocument.Create();
        prefab.Entities.AddRange(entities);
        return prefab;
    }

    private static EntityDocument Entity(string name, params ComponentDocument[] components) =>
        new() { Id = Guid.NewGuid(), Name = name, Components = [.. components] };

    private static EntityDocument Instance(AssetGuid prefab, string name, params ComponentDocument[] components) =>
        new() { Id = Guid.NewGuid(), Name = name, Prefab = new PrefabLink { Asset = prefab }, Components = [.. components] };

    private static ComponentDocument Transform(float x, float y) => new("Transform", new JsonObject { ["position"] = new JsonArray(x, y) });

    public enum Kind
    {
        First,
        Second
    }

    [Flags]
    public enum Options
    {
        None = 0,
        A = 1,
        B = 2,
        C = 4
    }

    public sealed record Nested(int Count, string Label);

    public struct Inner
    {
        public Vector2 Offset;
        public Color Color;
    }

    [Component]
    public struct Probe
    {
        public bool Flag;
        public byte Small;
        public int Number;
        public long Big;
        public ulong Huge;
        public float Ratio;
        public double Precise;
        public decimal Money;
        public string? Text;
        public Guid Key;
        public Kind Kind;
        public Options Flags;
        public Vector2 Point;
        public Color Tint;
        public Rect2 Area;
        public Curve? Curve;
        public Gradient? Gradient;
        public int? Maybe;
        public Rect2? MaybeArea;
        public float[]? Samples;
        public List<string>? Names;
        public IReadOnlyList<Vector2>? Path;
        public ImmutableArray<int> Steps;
        public HashSet<string>? Set;
        public Nested? Nested;
        public Inner Inner;
        public AssetGuid Reference;
        public Entity Other;
        public Texture Texture;
        public TextureAsset? Sheet;
        public TileMap? Map;
    }

    private sealed class StubContext(params object[] assets) : IInstantiationContext, ICaptureContext
    {
        public IServiceProvider Services => throw new NotSupportedException();

        public T? GetAsset<T>(AssetGuid guid) where T : class => assets.OfType<T>().FirstOrDefault();

        public Entity GetEntity(Guid id) => Ecs.Entity.Null;

        public AssetGuid GetGuid(object asset) => default;

        public Guid GetEntityId(Entity entity) => Guid.Empty;
    }

    private sealed class NoScenes : ISceneManager
    {
        public Runtime.Scenes.Scene? Current => null;

        public bool IsLoading => false;

        public SceneLoadProgress? LoadProgress => null;

        public IReadOnlyCollection<string> RegisteredScenes => [];

        public Task LoadAsync(SceneRequest request, SceneTransition? transition = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ReloadAsync(SceneTransition? transition = null) => Task.CompletedTask;
    }
}
