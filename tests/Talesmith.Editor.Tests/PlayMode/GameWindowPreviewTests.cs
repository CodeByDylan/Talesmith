using System.Text.Json.Nodes;
using Talesmith.Editor.Commands;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Shell;

namespace Talesmith.Editor.Tests.PlayMode;

public sealed class GameWindowPreviewTests
{
    [Fact]
    public void APreviewStartsOffAtFullHdFittedToThePanel() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var preview = editor.Get<GameWindowPreview>();

        Assert.False(preview.IsEnabled);
        Assert.Equal(new GameWindowSize(1920, 1080), preview.Size);
        Assert.Equal("Full HD", preview.Preset?.Name);
        Assert.Equal(GameWindowZoom.Fit, preview.Zoom);
        Assert.Equal("Game window", preview.Presets[0].Name);
    });

    [Fact]
    public void ACommandTurnsThePreviewOnAndOff() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        editor.Get<ShellViewModel>();
        var commands = editor.Get<EditorCommandRegistry>();
        var preview = editor.Get<GameWindowPreview>();

        Assert.True(commands.TryExecute("play.windowPreview"));
        Assert.True(preview.IsEnabled);
        Assert.True(commands.TryExecute("play.windowPreview"));
        Assert.False(preview.IsEnabled);
    });

    [Fact]
    public void ThePresetFollowsTheSizeTurnedEitherWayAndEditsMakeASizeOfItsOwn() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var preview = editor.Get<GameWindowPreview>();
        var phone = preview.Presets.Single(p => p.Name == "Phone");

        preview.Preset = phone;
        Assert.Equal(new GameWindowSize(1170, 2532, 3), preview.Size);
        preview.RotateCommand.Execute(null);
        Assert.Equal(new GameWindowSize(2532, 1170, 3), preview.Size);
        Assert.Same(phone, preview.Preset);
        Assert.Equal("19.5:9", preview.AspectRatio);

        preview.Width = 2000;
        Assert.Null(preview.Preset);
        preview.Height = 10;
        Assert.Equal(new GameWindowSize(2000, 64, 3), preview.Size);
        preview.DisplayScale = 1.25;
        Assert.Equal(1.25, preview.Size.DisplayScale);
        preview.DisplayScale = 9;
        Assert.Equal(1, preview.Size.DisplayScale);
    });

    [Fact]
    public void TheFittedZoomShowsOnlyWhileFitting() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var preview = editor.Get<GameWindowPreview>();

        preview.ActualZoom = 0.374;
        Assert.Equal("37%", preview.FittedZoomText);
        preview.Zoom = GameWindowZoom.All.Single(z => z.Factor == 2);
        Assert.Equal("", preview.FittedZoomText);
        Assert.Equal("200%", preview.Zoom.Name);
    });

    [Fact]
    public void TheProjectKeepsThePreviewAndBringsBackOnlyValuesInRange() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var project = editor.Get<IProjectService>();
        var state = editor.Get<ProjectState>();
        var preview = editor.Get<GameWindowPreview>();
        preview.IsEnabled = true;
        preview.Size = new GameWindowSize(2560, 1440, 1.5);
        preview.Zoom = GameWindowZoom.All.Single(z => z.Factor == 0.5);
        await state.SaveAsync();

        using (var restored = new GameWindowPreview(project, new ProjectState(project.Project)))
        {
            Assert.True(restored.IsEnabled);
            Assert.Equal(new GameWindowSize(2560, 1440, 1.5), restored.Size);
            Assert.Equal(0.5, restored.Zoom.Factor);
        }

        state.Set("game.window", new JsonObject { ["enabled"] = true, ["width"] = 99_999, ["height"] = 5, ["displayScale"] = 9, ["zoom"] = 0.33 });
        await state.SaveAsync();
        using var clamped = new GameWindowPreview(project, new ProjectState(project.Project));
        Assert.Equal(new GameWindowSize(7680, 64), clamped.Size);
        Assert.Equal(GameWindowZoom.Fit, clamped.Zoom);
    });

    [Fact]
    public void TheProjectsPresetsFollowItsSettings() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var preview = editor.Get<GameWindowPreview>();

        await editor.Get<IProjectService>().UpdateSettingsAsync(json =>
        {
            json["windowWidth"] = 1600;
            json["windowHeight"] = 900;
        });

        await editor.WaitAsync(() => preview.Presets[0].Size == new GameWindowSize(1600, 900));
        preview.Size = new GameWindowSize(1600, 900);
        Assert.Equal("Game window", preview.Preset?.Name);
    });
}
