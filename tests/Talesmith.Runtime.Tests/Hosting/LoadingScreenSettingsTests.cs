using Talesmith.Mathematics;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Tests.Hosting;

public sealed class LoadingScreenSettingsTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("talesmith-settings-");

    public LoadingScreenSettingsTests() => Directory.CreateDirectory(Path.Combine(_folder.FullName, "config"));

    public void Dispose() => _folder.Delete(true);

    [Fact]
    public void GamesWithoutALoadingScreenSectionShowTheirTitleOnTheirClearColor()
    {
        var settings = Load("""{ "title": "Hex Quest" }""").LoadingScreen;

        Assert.Null(settings.BackgroundColor);
        Assert.Null(settings.Image);
        Assert.Null(settings.ForegroundColor);
        Assert.True(settings.BetweenScenes);
    }

    [Fact]
    public void ReadsTheLoadingScreenSection()
    {
        var settings = Load("""
            {
              // Comments are allowed, as everywhere in game.json.
              "loadingScreen": { "backgroundColor": "#101820", "image": "ui/logo.png", "foregroundColor": "#F0C060", "betweenScenes": false }
            }
            """).LoadingScreen;

        Assert.Equal(new Color(0x10, 0x18, 0x20), settings.BackgroundColor);
        Assert.Equal("ui/logo.png", settings.Image);
        Assert.Equal(new Color(0xF0, 0xC0, 0x60), settings.ForegroundColor);
        Assert.False(settings.BetweenScenes);
    }

    [Fact]
    public void ANullSectionMeansTheDefaults() => Assert.Equal(new LoadingScreenSettings(), Load("""{ "loadingScreen": null }""").LoadingScreen);

    [Fact]
    public void ABadColorNamesTheFile()
    {
        var error = Assert.Throws<InvalidDataException>(() => Load("""{ "loadingScreen": { "backgroundColor": "dark" } }"""));

        Assert.Contains(GameSettings.FileName, error.Message.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("'dark' is not a color", error.Message, StringComparison.Ordinal);
    }

    private GameSettings Load(string json)
    {
        File.WriteAllText(Path.Combine(_folder.FullName, GameSettings.FileName), json);
        return GameSettings.Load(_folder.FullName);
    }
}
