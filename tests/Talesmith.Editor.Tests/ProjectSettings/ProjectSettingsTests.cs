using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Projects;
using Talesmith.Editor.ProjectSettings;
using Talesmith.Input;
using Talesmith.Physics;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Tests.ProjectSettings;

public sealed class ProjectSettingsTests
{
    private static readonly ViewSettings PlatformerView = new()
    {
        Width = 640,
        Height = 360,
        ScaleMode = ViewScaleMode.Fit,
        IntegerScale = true,
        OverlayWidth = 1280,
        OverlayHeight = 720
    };

    [Fact]
    public void SavedSettingsReadBackAndNewGamesUseThePhysicsLayers() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);

        settings.Game.Title = "Renamed Quest";
        settings.Game.WindowWidth = 1600;
        settings.Input.AddActionCommand.Execute(null);
        var action = settings.Input.SelectedAction!;
        action.Name = "Dash";
        settings.Input.StartRecordingCommand.Execute(action.Bindings[0].Slots[0]);
        Assert.True(settings.Input.Record(Key.LeftShift, KeyModifiers.None));
        settings.Physics.Layers[1].Name = "Player";
        settings.Physics.Layers[2].Name = "Enemies";
        settings.Physics.SetCollision(1, 2, false);
        Assert.True(settings.IsDirty);

        Assert.True(await settings.SaveAsync());

        Assert.False(settings.IsDirty);
        Assert.Equal("Renamed Quest", project.Settings.Title);
        var reopened = Open(editor);
        Assert.Equal("Renamed Quest", reopened.Game.Title);
        Assert.Equal(1600, reopened.Game.WindowWidth);
        var dash = Assert.Single(reopened.Input.Actions, a => a.Name == "Dash");
        Assert.Equal(Key.LeftShift, Assert.IsType<KeyBinding>(Assert.Single(dash.ToDefinition().Bindings)).Key);
        Assert.Equal("Player", reopened.Physics.Layers[1].Name);
        Assert.False(reopened.Physics.ShouldCollide(2, 1));
        Assert.True(reopened.Physics.ShouldCollide(1, 1));

        await using var session = await project.CreateSessionAsync(new GameSessionRequest(GameSessionKind.Play));
        var physics = session.Game.Services.GetRequiredService<PhysicsSettings>();
        Assert.False(physics.ShouldLayersCollide(1, 2));
        Assert.False(physics.ShouldLayersCollide(2, 1));
        Assert.True(physics.ShouldLayersCollide(1, 3));
    });

    [Fact]
    public void ActionsWithTheSameNameAreNotSaved() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var settings = Open(editor);
        var path = Path.Combine(editor.Get<IProjectService>().Project.AssetRoot, editor.Get<IProjectService>().Settings.InputProfile);
        var before = await File.ReadAllTextAsync(path);

        settings.Input.AddActionCommand.Execute(null);
        settings.Input.SelectedAction!.Name = settings.Input.Actions[0].Name;

        Assert.False(await settings.SaveAsync());
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    });

    [Fact]
    public void ViewSettingsSaveIntoTheViewSectionAndReadBack() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);
        Assert.Equal(PlatformerView, project.Settings.View);

        settings.Game.ScaleModeIndex = GameSettingsPage.ScaleModes.ToList().IndexOf(ViewScaleMode.Expand);
        settings.Game.ViewWidth = 960;
        settings.Game.IntegerScale = false;
        settings.Game.BorderColor = global::Avalonia.Media.Color.FromRgb(0x10, 0x20, 0x30);
        settings.Game.HasOverlaySize = false;
        Assert.True(settings.IsDirty);
        Assert.Equal("A 1280×720 window shows 960×540 units at 1.33×.", settings.Game.ViewPreview);

        Assert.True(await settings.SaveAsync());

        var expected = new ViewSettings { Width = 960, Height = 360, ScaleMode = ViewScaleMode.Expand, BorderColor = new Mathematics.Color(0x10, 0x20, 0x30) };
        Assert.Equal(expected, project.Settings.View);
        var reopened = Open(editor).Game;
        Assert.Equal(ViewScaleMode.Expand, reopened.ScaleMode);
        Assert.Equal(960, reopened.ViewWidth);
        Assert.Equal(360, reopened.ViewHeight);
        Assert.False(reopened.IntegerScale);
        Assert.False(reopened.HasBorders);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(project.Project.SettingsFile))!;
        Assert.Equal("expand", json["view"]!["scaleMode"]!.GetValue<string>());
        Assert.Equal(1280, json["windowWidth"]!.GetValue<int>());
        Assert.False(json["view"]!.AsObject().ContainsKey("overlayWidth"));
        Assert.False(json["view"]!.AsObject().ContainsKey("overlayHeight"));
    });

    [Fact]
    public void TheOverlaySizeSavesIntoTheViewSectionAndReadsBack() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);
        Assert.True(settings.Game.HasOverlaySize);
        Assert.Equal(1280, settings.Game.OverlayWidth);
        Assert.Equal(720, settings.Game.OverlayHeight);
        Assert.Equal("A 1280×720 window shows 640×360 units at 2×. Overlays lay out in 1280×720 units at 1×.", settings.Game.ViewPreview);

        settings.Game.OverlayWidth = 1920;
        settings.Game.OverlayHeight = 1080;
        Assert.True(settings.IsDirty);
        Assert.True(await settings.SaveAsync());

        Assert.Equal(PlatformerView with { OverlayWidth = 1920, OverlayHeight = 1080 }, project.Settings.View);
        var reopened = Open(editor).Game;
        Assert.True(reopened.HasOverlaySize);
        Assert.Equal(1920, reopened.OverlayWidth);
        Assert.Equal(1080, reopened.OverlayHeight);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(project.Project.SettingsFile))!;
        Assert.Equal(1920, json["view"]!["overlayWidth"]!.GetValue<int>());
        Assert.Equal(1080, json["view"]!["overlayHeight"]!.GetValue<int>());
    });

    [Fact]
    public void AnOverlaySizeIsAddedToAViewSectionWithoutOne() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder =>
        {
            var path = Path.Combine(folder, "assets", GameSettings.FileName);
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json["view"]!.AsObject().Remove("overlayWidth");
            json["view"]!.AsObject().Remove("overlayHeight");
            File.WriteAllText(path, json.ToJsonString());
        });
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);
        Assert.False(settings.Game.HasOverlaySize);
        Assert.Equal("A 1280×720 window shows 640×360 units at 2×.", settings.Game.ViewPreview);

        settings.Game.HasOverlaySize = true;
        settings.Game.OverlayWidth = 1280;
        settings.Game.OverlayHeight = 720;
        Assert.True(await settings.SaveAsync());

        Assert.Equal(PlatformerView, project.Settings.View);
    });

    [Fact]
    public void AViewSectionIsCreatedForGamesWithoutOne() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder =>
        {
            var path = Path.Combine(folder, "assets", GameSettings.FileName);
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            json.Remove("view");
            File.WriteAllText(path, json.ToJsonString());
        });
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);
        Assert.Equal(ViewScaleMode.None, settings.Game.ScaleMode);
        Assert.False(settings.Game.IsViewScaled);
        Assert.False(settings.IsDirty);

        settings.Game.ScaleModeIndex = GameSettingsPage.ScaleModes.ToList().IndexOf(ViewScaleMode.Fit);
        Assert.True(await settings.SaveAsync());

        Assert.Equal(ViewSettings.Unscaled with { ScaleMode = ViewScaleMode.Fit }, project.Settings.View);
    });

    [Fact]
    public void InvalidViewSizesAreReportedAndNotSaved() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);

        settings.Game.ViewHeight = 0;

        Assert.Equal("The view height must be positive, but is 0.", settings.Game.ViewError);
        Assert.Equal("", settings.Game.ViewPreview);
        Assert.False(await settings.SaveAsync());
        Assert.Equal(360, project.Settings.View.Height);
    });

    [Fact]
    public void TheLoadingScreenIsSavedAsItsOwnSectionAndRemovedOnceItIsBackToTheDefaults() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var settings = Open(editor);
        var game = settings.Game;
        Assert.Equal(GameSettingsPage.NoImage, game.LoadingImage);
        Assert.Contains("sprites/hero.png", game.LoadingImages);

        game.LoadingImage = "sprites/hero.png";
        game.HasLoadingBackground = true;
        game.LoadingBackground = global::Avalonia.Media.Color.FromRgb(0x10, 0x18, 0x20);
        game.LoadingBetweenScenes = false;
        Assert.True(settings.IsDirty);
        Assert.Equal("sprites/hero.png", game.LoadingPreview.LoadingScreen.Image);
        Assert.True(await settings.SaveAsync());

        var screen = project.Settings.LoadingScreen;
        Assert.Equal("sprites/hero.png", screen.Image);
        Assert.Equal(new Mathematics.Color(0x10, 0x18, 0x20), screen.BackgroundColor);
        Assert.Null(screen.ForegroundColor);
        Assert.False(screen.BetweenScenes);
        var reopened = Open(editor).Game;
        Assert.Equal("sprites/hero.png", reopened.LoadingImage);
        Assert.True(reopened.HasLoadingBackground);
        Assert.False(reopened.HasLoadingForeground);

        reopened.LoadingImage = GameSettingsPage.NoImage;
        reopened.HasLoadingBackground = false;
        reopened.LoadingBetweenScenes = true;
        await reopened.SaveAsync();

        Assert.Equal(new LoadingScreenSettings(), project.Settings.LoadingScreen);
        Assert.DoesNotContain("loadingScreen", await File.ReadAllTextAsync(project.Project.SettingsFile), StringComparison.Ordinal);
    });

    private static ProjectSettingsViewModel Open(EditorFixture editor) =>
        new(editor.Get<IProjectService>(), editor.Get<IDialogService>(), editor.Get<IToastService>(), () => Task.CompletedTask, () => { });
}
