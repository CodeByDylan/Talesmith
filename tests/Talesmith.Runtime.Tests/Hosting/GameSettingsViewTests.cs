using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Tests.Hosting;

/// <summary>The <c>view</c> section of <c>config/game.json</c>.</summary>
public sealed class GameSettingsViewTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("talesmith-settings").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void GamesWithoutAViewSectionAreUnscaled()
    {
        var settings = Load("""{ "title": "Old game" }""");

        Assert.Equal(ViewScaleMode.None, settings.View.ScaleMode);
        Assert.Same(ViewSettings.Unscaled, new GameSettings().View);
    }

    [Fact]
    public void ReadsEveryViewSetting()
    {
        var settings = Load("""
            {
              "view": { "width": 640, "height": 360, "scaleMode": "expand", "integerScale": true, "borderColor": "#102030" }
            }
            """);

        Assert.Equal(new ViewSettings { Width = 640, Height = 360, ScaleMode = ViewScaleMode.Expand, IntegerScale = true, BorderColor = new Color(0x10, 0x20, 0x30) },
            settings.View);
    }

    [Fact]
    public void AViewSectionDefaultsToFitOn1280By720WithBlackBars()
    {
        var settings = Load("""{ "view": {} }""");

        Assert.Equal(new ViewSettings { Width = 1280, Height = 720, ScaleMode = ViewScaleMode.Fit, IntegerScale = false, BorderColor = Color.Black },
            settings.View);
    }

    [Fact]
    public void ReadsTheOverlaySize()
    {
        var view = Load("""{ "view": { "width": 640, "height": 360, "overlayWidth": 1280, "overlayHeight": 720 } }""").View;

        Assert.Equal(1280, view.OverlayWidth);
        Assert.Equal(720, view.OverlayHeight);
        Assert.Equal(2, view.OverlayUnitsPerViewUnit());
    }

    [Fact]
    public void WithoutAnOverlaySizeOverlaysUseViewUnits()
    {
        var view = Load("""{ "view": { "width": 640, "height": 360 } }""").View;

        Assert.Null(view.OverlayWidth);
        Assert.Null(view.OverlayHeight);
        Assert.Equal(1, view.OverlayUnitsPerViewUnit());
    }

    [Theory]
    [InlineData(ViewScaleMode.Fit, 1280, 720, 2)]
    [InlineData(ViewScaleMode.Expand, 1280, 800, 2)]
    [InlineData(ViewScaleMode.Crop, 960, 720, 1.5)]
    [InlineData(ViewScaleMode.None, 1280, 720, 1)]
    public void OverlayUnitsPerViewUnitFitTheOverlaySizeInTheDesignSize(ViewScaleMode mode, int width, int height, float expected) =>
        Assert.Equal(expected, new ViewSettings { Width = 640, Height = 360, ScaleMode = mode, OverlayWidth = width, OverlayHeight = height }.OverlayUnitsPerViewUnit());

    [Theory]
    [InlineData("Fit", ViewScaleMode.Fit)]
    [InlineData("crop", ViewScaleMode.Crop)]
    [InlineData("none", ViewScaleMode.None)]
    public void ScaleModesIgnoreCase(string text, ViewScaleMode mode) =>
        Assert.Equal(mode, Load($$"""{ "view": { "scaleMode": "{{text}}" } }""").View.ScaleMode);

    [Theory]
    [InlineData("""{ "view": { "width": 0 } }""", "view width must be positive")]
    [InlineData("""{ "view": { "height": -720 } }""", "view height must be positive")]
    [InlineData("""{ "view": { "scaleMode": "stretch" } }""", "\"stretch\" is not a view scale mode; use fit, expand, crop or none")]
    [InlineData("""{ "view": { "scaleMode": 2 } }""", "\"2\" is not a view scale mode")]
    [InlineData("""{ "view": { "overlayWidth": 1280 } }""", "overlay width but no overlay height; set both or neither")]
    [InlineData("""{ "view": { "overlayHeight": 720 } }""", "overlay height but no overlay width; set both or neither")]
    [InlineData("""{ "view": { "overlayWidth": 0, "overlayHeight": 720 } }""", "view overlay width must be positive")]
    [InlineData("""{ "view": { "overlayWidth": 1280, "overlayHeight": -1 } }""", "view overlay height must be positive")]
    public void InvalidViewsAreReportedClearly(string json, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => Load(json));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
        Assert.Contains("game.json", error.Message, StringComparison.Ordinal);
    }

    private GameSettings Load(string json)
    {
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        File.WriteAllText(Path.Combine(_root, GameSettings.FileName), json);
        return GameSettings.Load(_root);
    }
}
