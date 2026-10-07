using Talesmith.Runtime.Serialization;

namespace Talesmith.Lighting.Tests;

public sealed class SceneLightingSettingsTests
{
    [Fact]
    public void SettingsRoundTripThroughTheSceneFile()
    {
        var scene = SceneDocument.Create();
        new SceneLightingSettings
        {
            Quality = LightingQuality.Custom,
            CustomQuality = new LightingQualitySettings(0.75f, 20, 6, 768, 3, 128),
            LitLayerLimit = 450,
            TileMapShadows = false,
            TileMapShadowLayer = 4
        }.Write(scene.Environment);

        var json = DocumentSerializer.Write(scene);
        var read = SceneLightingSettings.Read(DocumentSerializer.Default.ReadScene(json).Environment);

        Assert.Contains("\"lighting\"", json, StringComparison.Ordinal);
        Assert.Equal(LightingQuality.Custom, read.Quality);
        Assert.Equal(new LightingQualitySettings(0.75f, 20, 6, 768, 3, 128), read.CustomQuality);
        Assert.Equal(450, read.LitLayerLimit);
        Assert.False(read.TileMapShadows);
        Assert.Equal(4, read.TileMapShadowLayer);

        var environment = new LightingEnvironment();
        read.ApplyTo(environment);
        Assert.Equal(LightingQuality.Custom, environment.Quality);
        Assert.Equal(20, environment.QualitySettings.MaxLights);
        Assert.Equal(450, environment.LitLayerLimit);
        Assert.False(environment.TileMapShadows);
    }

    [Fact]
    public void ScenesWithoutSettingsUseTheDefaults()
    {
        var read = SceneLightingSettings.Read(new SceneEnvironment());
        var environment = new LightingEnvironment();
        read.ApplyTo(environment);

        Assert.Equal(LightingQuality.Medium, environment.Quality);
        Assert.Equal(Rendering.RenderLayers.Overlay, environment.LitLayerLimit);
        Assert.True(environment.TileMapShadows);
    }
}
