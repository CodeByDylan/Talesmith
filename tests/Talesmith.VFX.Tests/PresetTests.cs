using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Assets;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.VFX.Presets;

namespace Talesmith.VFX.Tests;

public sealed class PresetTests
{
    public static TheoryData<string> BuiltIn => [.. BuiltInParticlePresets.All.Select(p => p.Name)];

    [Theory]
    [MemberData(nameof(BuiltIn))]
    public void BuiltInPresetsRoundTripThroughJson(string name)
    {
        var preset = BuiltInParticlePresets.Find(name)!.CreatePreset();

        var json = ParticlePresetSerializer.Serialize(preset);
        var loaded = ParticlePresetSerializer.Deserialize(json);

        Assert.Equal(name, loaded.Name);
        Assert.Equal(json, ParticlePresetSerializer.Serialize(loaded));
    }

    [Fact]
    public void RoundTripKeepsEveryKindOfValue()
    {
        var settings = new ParticleSettings
        {
            Duration = 3.5f,
            Looping = false,
            Seed = 99,
            SimulationSpace = ParticleSimulationSpace.Local,
            Culling = ParticleCullingMode.AlwaysSimulate
        };
        settings.Emission.Bursts.Add(new ParticleBurst(0.25f, new MinMaxFloat(3, 7), cycles: 4, interval: 0.2f, probability: 0.5f));
        settings.Shape.Kind = ParticleShapeKind.Polygon;
        settings.Shape.Points = [new Vector2(0, 0), new Vector2(10, 0), new Vector2(5, 8)];
        settings.Initial.Color = new MinMaxColor(new Color(10, 20, 30, 40), Color.White);
        settings.SizeOverLifetime.Size = new Curve([new CurveKey(0, 1, 0, 2), new CurveKey(1, 0, -1, 0, CurveInterpolation.Linear)]);
        settings.ColorOverLifetime.Color = Gradient.Between(Color.Black, new Color(255, 0, 0, 0));
        settings.Renderer.Texture = new AssetGuid(Guid.NewGuid());
        settings.Renderer.Sprite = "spark";
        settings.Renderer.Blend = BlendMode.Additive;

        var copy = ParticlePresetSerializer.Deserialize(ParticlePresetSerializer.Serialize(new ParticlePreset("All", settings))).Settings;

        Assert.Equal(3.5f, copy.Duration);
        Assert.False(copy.Looping);
        Assert.Equal(99, copy.Seed);
        Assert.Equal(ParticleSimulationSpace.Local, copy.SimulationSpace);
        Assert.Equal(ParticleCullingMode.AlwaysSimulate, copy.Culling);
        var burst = Assert.Single(copy.Emission.Bursts);
        Assert.Equal((0.25f, new MinMaxFloat(3, 7), 4, 0.2f, 0.5f), (burst.Time, burst.Count, burst.Cycles, burst.Interval, burst.Probability));
        Assert.Equal(settings.Shape.Points, copy.Shape.Points);
        Assert.Equal(settings.Initial.Color, copy.Initial.Color);
        Assert.Equal(settings.SizeOverLifetime.Size, copy.SizeOverLifetime.Size);
        Assert.Equal(settings.ColorOverLifetime.Color, copy.ColorOverLifetime.Color);
        Assert.Equal(settings.Renderer.Texture, copy.Renderer.Texture);
        Assert.Equal("spark", copy.Renderer.Sprite);
        Assert.Equal(BlendMode.Additive, copy.Renderer.Blend);
    }

    [Fact]
    public void FileIsReadableJson()
    {
        var json = JsonNode.Parse(ParticlePresetSerializer.Serialize(BuiltInParticlePresets.All[0].CreatePreset()))!;

        Assert.Equal(1, json["version"]!.GetValue<int>());
        Assert.Equal("Fire", json["name"]!.GetValue<string>());
        Assert.Equal(90, json["settings"]!["emission"]!["rateOverTime"]!.GetValue<float>());
        Assert.Equal("additive", json["settings"]!["renderer"]!["blend"]!.GetValue<string>());
        Assert.Null(json["settings"]!["renderer"]!["texture"]);
    }

    [Fact]
    public void MissingFieldsKeepTheirDefaults()
    {
        var preset = ParticlePresetSerializer.Deserialize("""{ "version": 1, "name": "Tiny", "settings": { "emission": { "rateOverTime": 3 }, "initial": { "size": [2, 4] } } }""");

        Assert.Equal(3, preset.Settings.Emission.RateOverTime);
        Assert.Equal(new MinMaxFloat(2, 4), preset.Settings.Initial.Size);
        Assert.Equal(new ParticleSettings().Duration, preset.Settings.Duration);
        Assert.True(preset.Settings.Renderer.Enabled);
    }

    [Fact]
    public void RangesAreWrittenAsObjectsAndReadFromCompactForms()
    {
        var preset = ParticlePresetSerializer.Deserialize("""{ "settings": { "initial": { "lifetime": 2, "color": "#FF0000" }, "renderer": { "blend": "Additive" } } }""");

        var json = ParticlePresetSerializer.SettingsToJson(preset.Settings);

        Assert.Equal(new MinMaxFloat(2), preset.Settings.Initial.Lifetime);
        Assert.Equal(BlendMode.Additive, preset.Settings.Renderer.Blend);
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["min"] = 2f, ["max"] = 2f }, json["initial"]!["lifetime"]));
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["from"] = "#FF0000", ["to"] = "#FF0000" }, json["initial"]!["color"]));
    }

    [Fact]
    public void NewerVersionsAreRejected()
    {
        Assert.Throws<JsonException>(() => ParticlePresetSerializer.Deserialize("""{ "version": 99, "settings": {} }"""));
    }

    [Fact]
    public void PluginModulesAreSavedByTheirStableName()
    {
        var registry = new ParticleModuleRegistry([new ParticleModuleRegistration("Tests.Wind", typeof(Wind), "Wind")]);
        var settings = new ParticleSettings();
        settings.CustomModules.Add(new Wind { Strength = 12.5f });

        var json = ParticlePresetSerializer.Serialize(new ParticlePreset("Windy", settings), registry);
        var loaded = ParticlePresetSerializer.Deserialize(json, registry).Settings;

        Assert.Contains("\"Tests.Wind\"", json);
        Assert.Equal(12.5f, Assert.IsType<Wind>(Assert.Single(loaded.CustomModules)).Strength);
    }

    [Fact]
    public void UnknownModulesAreKeptVerbatim()
    {
        var registry = new ParticleModuleRegistry([new ParticleModuleRegistration("Tests.Wind", typeof(Wind), "Wind")]);
        var settings = new ParticleSettings();
        settings.CustomModules.Add(new Wind { Strength = 3 });
        var json = ParticlePresetSerializer.Serialize(new ParticlePreset("Windy", settings), registry);

        var withoutPlugin = ParticlePresetSerializer.Deserialize(json);
        var unknown = Assert.IsType<UnknownParticleModule>(Assert.Single(withoutPlugin.Settings.CustomModules));
        Assert.Equal("Tests.Wind", unknown.TypeName);
        Assert.Equal(json, ParticlePresetSerializer.Serialize(withoutPlugin));
    }

    [Fact]
    public void CloneIsADeepCopy()
    {
        var original = BuiltInParticlePresets.Sparks();
        original.CustomModules.Add(new Wind { Strength = 4 });

        var copy = original.Clone();
        copy.Emission.Bursts[0].Time = 9;
        copy.Shape.Radius = 123;

        Assert.Equal(0, original.Emission.Bursts[0].Time);
        Assert.NotEqual(123, original.Shape.Radius);
        Assert.Equal(4, Assert.IsType<Wind>(Assert.Single(copy.CustomModules)).Strength);
    }

    [Fact]
    public async Task ImporterLoadsPresetFiles()
    {
        var folder = Directory.CreateTempSubdirectory("talesmith-particles-");
        try
        {
            ParticlePresetSerializer.Save(Path.Combine(folder.FullName, "smoke.tparticles"), BuiltInParticlePresets.Find("Smoke")!.CreatePreset());
            var source = new FileSystemAssetSource(folder.FullName);
            var importer = new ParticlePresetImporter(ParticleModuleRegistry.Empty);
            var assets = new AssetManager(source, [importer], NullLogger<AssetManager>.Instance);

            var preset = await assets.LoadAsync<ParticlePreset>("smoke.tparticles", TestContext.Current.CancellationToken);

            Assert.Equal("Smoke", preset.Name);
            Assert.Equal(ParticleSortMode.OldestInFront, preset.Settings.Renderer.SortMode);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private sealed class Wind : IParticleModule
    {
        public bool Enabled { get; set; } = true;

        public float Strength = 1;

        public void Update(ParticleModuleContext context)
        {
        }
    }
}
