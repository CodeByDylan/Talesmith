using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Ecs;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;
using Talesmith.VFX.Presets;

namespace Talesmith.VFX.Tests;

public sealed class SceneIntegrationTests : IAsyncDisposable
{
    private readonly Game _game;

    public SceneIntegrationTests()
    {
        var builder = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings());
        builder.Services.AddTalesmithParticles();
        builder.Services.AddParticleModule<SwirlModule>("tests.swirl");
        _game = builder.Build();
    }

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public void EmittersAreDescribedForTheInspector()
    {
        var definition = _game.Services.GetRequiredService<ComponentRegistry>().Find(typeof(ParticleEmitter))!;

        Assert.Equal("Effects", definition.Info.Category);
        Assert.Contains(definition.Properties, p => p.Name == "settings" && p.Kind == PropertyKind.Object);
        Assert.DoesNotContain(definition.Properties, p => p.Name == "simulation");
    }

    [Fact]
    public async Task EmittersWithPluginModulesRoundTripThroughScenes()
    {
        var registry = _game.Services.GetRequiredService<ComponentRegistry>();
        var definition = registry.Find(typeof(ParticleEmitter))!;
        var data = definition.CreateDefault();
        var settings = (JsonObject)data["settings"]!;
        settings["maxParticles"] = 321;
        settings["customModules"] = new JsonArray(
            new JsonObject { ["type"] = "tests.swirl", ["data"] = new JsonObject { ["enabled"] = true, ["strength"] = 2.5 } },
            new JsonObject { ["type"] = "missing.plugin", ["data"] = new JsonObject { ["keep"] = "me" } });

        var scene = SceneDocument.Create();
        scene.Entities.Add(new EntityDocument { Id = Guid.NewGuid(), Name = "Fire", Components = [new ComponentDocument(definition.TypeName, data)] });
        var world = new World();
        var result = await _game.Services.GetRequiredService<SceneInstantiator>().InstantiateAsync(world, scene, TestContext.Current.CancellationToken);

        var emitter = world.Get<ParticleEmitter>(result.Entities[scene.Entities[0].Id]);
        Assert.Equal(321, emitter.Settings.MaxParticles);
        Assert.Equal(2.5f, Assert.IsType<SwirlModule>(emitter.Settings.CustomModules[0]).Strength);
        Assert.IsType<UnknownParticleModule>(emitter.Settings.CustomModules[1]);

        var captured = _game.Services.GetRequiredService<SceneCapture>().Capture(world);
        var modules = (JsonArray)captured.Entities[0].FindComponent(definition.TypeName)!.Data["settings"]!["customModules"]!;
        Assert.Equal("tests.swirl", (string?)modules[0]!["type"]);
        Assert.Equal("me", (string?)modules[1]!["data"]!["keep"]);
    }

    public static TheoryData<string> BuiltIn => [.. BuiltInParticlePresets.All.Select(p => p.Name)];

    public static TheoryData<string> SamplePresets => [.. Directory.GetFiles(SampleParticles(), "*" + ParticlePresetSerializer.Extension).Select(path => Path.GetFileName(path))];

    [Theory]
    [MemberData(nameof(BuiltIn))]
    public async Task PresetSettingsLoadInlineInScenesAndAreCapturedTheSame(string name)
    {
        var modules = _game.Services.GetRequiredService<ParticleModuleRegistry>();
        var settings = BuiltInParticlePresets.Find(name)!.CreatePreset().Settings;
        settings.CustomModules.Add(new SwirlModule { Strength = 3 });
        var json = ParticlePresetSerializer.SettingsToJson(settings, modules);

        var (emitter, captured) = await RoundTrip(json);

        Assert.Equal(json.ToJsonString(), ParticlePresetSerializer.SettingsToJson(emitter.Settings, modules).ToJsonString());
        Assert.True(JsonNode.DeepEquals(json, captured), $"Preset:\n{json}\nScene:\n{captured}");
    }

    [Theory]
    [MemberData(nameof(SamplePresets))]
    public async Task SamplePresetFilesLoadInlineInScenes(string file)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(SampleParticles(), file), TestContext.Current.CancellationToken);
        var preset = ParticlePresetSerializer.Deserialize(text);
        var settings = (JsonObject)JsonNode.Parse(text)!["settings"]!;

        var (emitter, _) = await RoundTrip(settings);

        Assert.Equal(ParticlePresetSerializer.SettingsToJson(preset.Settings).ToJsonString(), ParticlePresetSerializer.SettingsToJson(emitter.Settings).ToJsonString());
    }

    [Fact]
    public async Task CompactRangesInScenesLoadAndAreCapturedAsObjects()
    {
        var settings = new JsonObject { ["initial"] = new JsonObject { ["lifetime"] = 2, ["size"] = new JsonArray(3, 5), ["color"] = "#FF0000" } };

        var (emitter, captured) = await RoundTrip(settings);

        Assert.Equal(new MinMaxFloat(2), emitter.Settings.Initial.Lifetime);
        Assert.Equal(new MinMaxFloat(3, 5), emitter.Settings.Initial.Size);
        Assert.True(emitter.Settings.Initial.Color.IsConstant());
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["min"] = 2f, ["max"] = 2f }, captured["initial"]!["lifetime"]));
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["from"] = "#FF0000", ["to"] = "#FF0000" }, captured["initial"]!["color"]));
    }

    [Fact]
    public void RangesAreDescribedAsObjectsOfTheirFields()
    {
        var converters = _game.Services.GetRequiredService<ValueConverterRegistry>();

        var range = converters.Get<MinMaxFloat>().Describe(new PropertyDescriptor("size", "Size", PropertyKind.Object, typeof(MinMaxFloat)));
        var colors = converters.Get<MinMaxColor>().Describe(new PropertyDescriptor("color", "Color", PropertyKind.Object, typeof(MinMaxColor)));

        Assert.Equal(PropertyKind.Object, range.Kind);
        Assert.Equal(["min", "max"], range.Children.Select(c => c.Name));
        Assert.All(range.Children, c => Assert.Equal(PropertyKind.Number, c.Kind));
        Assert.Equal(PropertyKind.Object, colors.Kind);
        Assert.Equal(["from", "to"], colors.Children.Select(c => c.Name));
        Assert.Equal(["Color", "Or"], colors.Children.Select(c => c.Label));
        Assert.All(colors.Children, c => Assert.Equal(PropertyKind.Color, c.Kind));
    }

    private async Task<(ParticleEmitter Emitter, JsonObject Captured)> RoundTrip(JsonObject settings)
    {
        var definition = _game.Services.GetRequiredService<ComponentRegistry>().Find(typeof(ParticleEmitter))!;
        var data = definition.CreateDefault();
        data["settings"] = settings.DeepClone();
        var scene = SceneDocument.Create();
        scene.Entities.Add(new EntityDocument { Id = Guid.NewGuid(), Name = "Effect", Components = [new ComponentDocument(definition.TypeName, data)] });
        var world = new World();
        var result = await _game.Services.GetRequiredService<SceneInstantiator>().InstantiateAsync(world, scene, TestContext.Current.CancellationToken);

        var emitter = world.Get<ParticleEmitter>(result.Entities[scene.Entities[0].Id]);
        var captured = _game.Services.GetRequiredService<SceneCapture>().Capture(world);
        return (emitter, (JsonObject)captured.Entities[0].FindComponent(definition.TypeName)!.Data["settings"]!);
    }

    private static string SampleParticles()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Talesmith.slnx")))
                return Path.Combine(folder.FullName, "samples", "LanternGrove", "assets", "particles");
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }

    public sealed class SwirlModule : IParticleModule
    {
        public bool Enabled { get; set; } = true;

        public float Strength = 1;

        public void Update(ParticleModuleContext context)
        {
        }
    }
}
