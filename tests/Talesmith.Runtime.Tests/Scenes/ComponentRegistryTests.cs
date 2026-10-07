using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Authoring;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Tests.Scenes;

public sealed class ComponentRegistryTests : IDisposable
{
    private readonly SceneTestHost _host = new(services =>
    {
        services.AddComponent<Inspected>();
        services.AddComponent<PlainComponent>();
        services.AddComponent<Overridden>();
        services.AddSingleton<IComponentDefinition, OverriddenDefinition>();
    });

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData("Transform", typeof(Transform))]
    [InlineData("Sprite", typeof(Sprite))]
    [InlineData("SpriteAnimator", typeof(SpriteAnimator))]
    [InlineData("Camera", typeof(Camera))]
    [InlineData("TileMapRenderer", typeof(TileMapRenderer))]
    [InlineData("Tags", typeof(Tags))]
    [InlineData("Parent", typeof(Parent))]
    [InlineData("LocalTransform", typeof(LocalTransform))]
    [InlineData("Inactive", typeof(Inactive))]
    [InlineData("PrefabInstance", typeof(PrefabInstance))]
    [InlineData("SceneEntityId", typeof(SceneEntityId))]
    public void BuiltInComponentsAreFoundByNameAndType(string typeName, Type type)
    {
        Assert.True(_host.Components.TryGet(typeName, out var byName));
        Assert.True(_host.Components.TryGet(type, out var byType));
        Assert.Same(byName, byType);
        Assert.Equal(type, byName.ComponentType);
    }

    [Fact]
    public void TypeNamesAreShortForEngineComponentsAndFullOtherwise()
    {
        Assert.Equal("Sprite", ComponentRegistry.GetTypeName(typeof(Sprite)));
        Assert.Equal(typeof(PlainComponent).FullName, ComponentRegistry.GetTypeName(typeof(PlainComponent)));
        Assert.NotNull(_host.Components.Find(typeof(PlainComponent).FullName!));
    }

    [Fact]
    public void TransformDescribesItsProperties()
    {
        var properties = Properties<Transform>();

        Assert.Equal(["position", "rotation", "scale"], properties.Select(p => p.Name));
        Assert.Equal(PropertyKind.Vector2, properties[0].Kind);
        Assert.Equal(PropertyKind.Number, properties[1].Kind);
        Assert.True(properties[1].IsAngle);
        Assert.Equal("Core", _host.Components.Find(typeof(Transform))!.Info.Category);
    }

    [Fact]
    public void SpriteDescribesItsProperties()
    {
        var properties = Properties<Sprite>();
        var byName = properties.ToDictionary(p => p.Name);

        Assert.Equal(["texture", "sprite", "tint", "layer", "flipX", "flipY", "sortByY", "visible", "source", "size", "origin"], properties.Select(p => p.Name));
        Assert.Equal(PropertyKind.Asset, byName["texture"].Kind);
        Assert.Equal(typeof(TextureAsset), byName["texture"].AssetType);
        Assert.Equal(new TextureItemSource("texture", TextureItemKind.Sprite), byName["sprite"].TextureItems);
        Assert.True(byName["sprite"].IsNullable);
        Assert.Equal("Sprite", byName["sprite"].Label);
        Assert.Equal(PropertyKind.Color, byName["tint"].Kind);
        Assert.Equal(PropertyKind.Integer, byName["layer"].Kind);
        Assert.Equal(PropertyKind.Rect, byName["source"].Kind);
        Assert.Equal("Region", byName["source"].Header);
        Assert.Equal("Sort By Y", byName["sortByY"].Label);
        Assert.Equal("image", _host.Components.Find(typeof(Sprite))!.Info.Icon);
    }

    [Fact]
    public void SpriteAnimatorDescribesItsProperties()
    {
        var byName = Properties<SpriteAnimator>().ToDictionary(p => p.Name);

        Assert.Equal(["texture", "animation", "speed", "playing", "playOnStart"], byName.Keys);
        Assert.Equal(typeof(TextureAsset), byName["texture"].AssetType);
        Assert.Equal(new TextureItemSource("texture", TextureItemKind.Animation), byName["animation"].TextureItems);
        Assert.Equal(0, byName["speed"].Min);
        Assert.Equal(10, byName["speed"].Max);
        Assert.Equal(0.05, byName["speed"].Step);
    }

    [Fact]
    public void CameraDescribesItsProperties()
    {
        var byName = Properties<Camera>().ToDictionary(p => p.Name);

        Assert.Equal(["priority", "active", "target", "followSharpness", "bounds", "minZoom", "maxZoom", "zoom", "pixelSnap"], byName.Keys);
        Assert.Equal(PropertyKind.Entity, byName["target"].Kind);
        Assert.Equal("Follow target", byName["target"].Label);
        Assert.Equal(PropertyKind.Rect, byName["bounds"].Kind);
        Assert.True(byName["bounds"].IsNullable);
        Assert.Equal(PropertyKind.Number, byName["zoom"].Kind);
        Assert.Equal(PropertyKind.Boolean, byName["pixelSnap"].Kind);
    }

    [Fact]
    public void OtherBuiltInsDescribeTheirProperties()
    {
        var map = Assert.Single(Properties<TileMapRenderer>(), p => p.Name == "map");
        Assert.Equal(typeof(TileMap), map.AssetType);
        Assert.Equal([".hexy"], map.AssetExtensions);
        Assert.Equal(PropertyKind.Integer, Assert.Single(Properties<TileMapRenderer>(), p => p.Name == "renderLayer").Kind);

        var tags = Assert.Single(Properties<Tags>());
        Assert.Equal(PropertyKind.List, tags.Kind);
        Assert.Equal(PropertyKind.String, tags.Element!.Kind);

        Assert.Equal(PropertyKind.Entity, Assert.Single(Properties<Parent>()).Kind);
        Assert.Equal(["position", "rotation", "scale"], Properties<LocalTransform>().Select(p => p.Name));
        Assert.Empty(Properties<Inactive>());
        Assert.Equal(PropertyKind.Asset, Assert.Single(Properties<PrefabInstance>()).Kind);
        Assert.Equal(PropertyKind.String, Assert.Single(Properties<SceneEntityId>()).Kind);

        foreach (var hidden in new[] { typeof(Parent), typeof(LocalTransform), typeof(Inactive), typeof(PrefabInstance), typeof(SceneEntityId) })
            Assert.True(_host.Components.Find(hidden)!.Info.Hidden, hidden.Name);
    }

    [Fact]
    public void AuthoringAttributesShapeDescriptors()
    {
        var definition = _host.Components.Find(typeof(Inspected))!;
        var byName = definition.Properties.ToDictionary(p => p.Name);

        Assert.Equal("Inspected thing", definition.Info.DisplayName);
        Assert.Equal("Tests", definition.Info.Category);
        Assert.Equal("Inspected thing", definition.Info.DisplayName);
        Assert.DoesNotContain("cache", byName.Keys);
        Assert.Equal((0, 100, 5), (byName["health"].Min, byName["health"].Max, byName["health"].Step));
        Assert.Equal("Hit points", byName["health"].Label);
        Assert.Equal("How much damage it takes", byName["health"].Tooltip);
        Assert.Equal("Stats", byName["health"].Header);
        Assert.Equal(4, byName["notes"].Lines);
        Assert.True(byName["secret"].Hidden);
        Assert.Equal([".png", ".webp"], byName["icon"].AssetExtensions);
        Assert.Equal(PropertyKind.Asset, byName["icon"].Kind);
        Assert.True(byName["heading"].IsAngle);
        Assert.Null(byName["speed"].Max);
        Assert.Equal(1, byName["speed"].Min);

        Assert.Equal(PropertyKind.Enum, byName["mood"].Kind);
        Assert.Equal(["calm", "angry"], byName["mood"].EnumNames);
        Assert.True(byName["layers"].IsFlags);
        Assert.True(byName["limit"].IsNullable);
        Assert.Equal(PropertyKind.Integer, byName["limit"].Kind);

        Assert.Equal(PropertyKind.Object, byName["stats"].Kind);
        Assert.Equal(["strength", "names"], byName["stats"].Children.Select(c => c.Name));
        Assert.Equal(PropertyKind.List, byName["path"].Kind);
        Assert.Equal(PropertyKind.Vector2, byName["path"].Element!.Kind);
        Assert.Equal(PropertyKind.Curve, byName["falloff"].Kind);
        Assert.Equal(PropertyKind.Gradient, byName["colors"].Kind);
    }

    [Fact]
    public void CustomDefinitionsWinOverReflection()
    {
        Assert.IsType<OverriddenDefinition>(_host.Components.Find(typeof(Overridden)));
    }

    [Fact]
    public void DefaultsComeFromTheParameterlessConstructor()
    {
        var sprite = _host.Components.Find(typeof(Sprite))!.CreateDefault();

        Assert.Equal("#FFFFFF", sprite["tint"]!.GetValue<string>());
        Assert.True(sprite["visible"]!.GetValue<bool>());
        Assert.Null(sprite["texture"]);
        Assert.False(sprite.ContainsKey("size"), "size follows the texture unless set");
        Assert.Equal("[1,1]", _host.Components.Find(typeof(Transform))!.CreateDefault()["scale"]!.ToJsonString());
    }

    private IReadOnlyList<PropertyDescriptor> Properties<T>() => _host.Components.Find(typeof(T))!.Properties;

    [Flags]
    public enum Layers
    {
        None = 0,
        Ground = 1,
        Air = 2
    }

    public enum Mood
    {
        Calm,
        Angry
    }

    public sealed record Stats(int Strength, IReadOnlyList<string> Names);

    [Component("Inspected thing", Category = "Tests")]
    public struct Inspected
    {
        [Header("Stats")]
        [Label("Hit points")]
        [Tooltip("How much damage it takes")]
        [Range(0, 100, Step = 5)]
        public int Health;

        [Range(1)]
        public float Speed;

        [Multiline(4)]
        public string Notes;

        [HideInInspector]
        public string Secret;

        [Transient]
        public object Cache;

        [AssetFilter(".png", ".webp")]
        public AssetGuid Icon;

        [Angle]
        public float Heading;

        public Mood Mood;

        public Layers Layers;

        public int? Limit;

        public Stats? Stats;

        public List<Vector2> Path;

        public Curve Falloff;

        public Gradient Colors;
    }

    public struct PlainComponent
    {
        public int Value;
    }

    [Component]
    public struct Overridden
    {
        public int Value;
    }

    private sealed class OverriddenDefinition : IComponentDefinition
    {
        public string TypeName => "Overridden";

        public Type ComponentType => typeof(Overridden);

        public ComponentInfo Info { get; } = new("Overridden", "Tests");

        public IReadOnlyList<PropertyDescriptor> Properties => [];

        public JsonObject CreateDefault() => [];

        public IEnumerable<AssetDependency> GetDependencies(JsonObject data) => [];

        public void Apply(World world, Entity entity, JsonObject data, IInstantiationContext context) => world.Set(entity, new Overridden { Value = 42 });

        public JsonObject? Capture(World world, Entity entity, ICaptureContext context) => world.Has<Overridden>(entity) ? [] : null;

        public void Remove(World world, Entity entity) => world.Remove<Overridden>(entity);
    }
}
