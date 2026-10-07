using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Lighting;
using Talesmith.Editor.Undo;
using Talesmith.Lighting;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Tests.Lighting;

public sealed class LightingPanelTests
{
    [Fact]
    public void ThePanelListsLightsAndEditsTheAmbientLightAsUndoableEnvironmentChanges() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var lighting = editor.Get<LightingPanelViewModel>();
        var model = editor.Document;
        var torch = AddLight(model, "Torch", "point", castsShadows: true);
        AddLight(model, "Sun", "directional", castsShadows: false);

        Assert.Equal(["Torch", "Sun"], lighting.Lights.Select(l => l.Name));
        Assert.Single(lighting.LayerRows);
        lighting.TypeFilterIndex = 3;
        Assert.Equal(["Sun"], lighting.Lights.Select(l => l.Name));
        lighting.TypeFilterIndex = 0;

        var row = lighting.Lights.Single(l => l.Id == torch);
        row.Intensity = 2.5;
        Assert.Equal(2.5, JsonFormats.GetNumber(model.GetProperty(torch, LightingNames.Light, "intensity")!));

        var undo = editor.Get<IUndoService>();
        var steps = undo.UndoSteps.Count;
        lighting.AmbientIntensity = 0.4;
        lighting.AmbientIntensity = 0.3;
        Assert.Equal(0.3f, model.Document.Environment.AmbientIntensity);
        Assert.Equal(steps + 1, undo.UndoSteps.Count);

        await editor.World.WhenIdle;
        var environment = editor.World.Game!.Scenes.Current!.Services.GetRequiredService<LightingEnvironment>();
        Assert.Equal(0.3f, environment.AmbientIntensity);

        undo.Undo();
        Assert.Equal(1, model.Document.Environment.AmbientIntensity);
        Assert.Equal(1, lighting.AmbientIntensity);
    });

    [Fact]
    public void QualitySettingsAreSavedInTheSceneAndReachTheLightingEnvironment() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var lighting = editor.Get<LightingPanelViewModel>();
        lighting.QualityIndex = (int)LightingQuality.Custom;
        lighting.MaxLights = 12;
        lighting.TileMapShadows = false;

        Assert.True(await editor.Get<ISceneDocumentService>().SaveAsync());
        var file = Path.Combine(editor.Get<Editor.Projects.IProjectService>().Project.AssetRoot, editor.Document.Path!);
        var saved = SceneLightingSettings.Read(DocumentSerializer.Default.ReadScene(await File.ReadAllTextAsync(file)).Environment);
        Assert.Equal(LightingQuality.Custom, saved.Quality);
        Assert.Equal(12, saved.CustomQuality!.MaxLights);
        Assert.False(saved.TileMapShadows);

        await editor.WaitAsync(() => !editor.World.IsBusy);
        await editor.World.WhenIdle;
        var environment = editor.World.Game!.Scenes.Current!.Services.GetRequiredService<LightingEnvironment>();
        Assert.Equal(LightingQuality.Custom, environment.Quality);
        Assert.Equal(12, environment.QualitySettings.MaxLights);
        Assert.False(environment.TileMapShadows);

        lighting.IsLightingOff = true;
        Assert.False(environment.Enabled);
        Assert.Equal(LightingQuality.Custom, SceneLightingSettings.Read(editor.Document.Document.Environment).Quality);
        lighting.IsLightingOff = false;
        Assert.True(environment.Enabled);
    });

    private static Guid AddLight(SceneDocumentModel model, string name, string type, bool castsShadows) =>
        model.CreateEntity(name, null,
        [
            new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }),
            new ComponentDocument(LightingNames.Light, new JsonObject { ["type"] = type, ["castsShadows"] = castsShadows })
        ]).Id;
}
