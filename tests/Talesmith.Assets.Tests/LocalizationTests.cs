using System.Text;
using Talesmith.Assets.Localization;
using Talesmith.Events;

namespace Talesmith.Assets.Tests;

public sealed class LocalizationTests : IDisposable
{
    private const string Strings = """
        {
          "version": 1,
          "strings": {
            "menu.start": { "en": "Start", "nl": "Beginnen", "pt": "Começar" },
            "menu.quit": { "en": "Quit" },
            "hud.coins": { "en": "{0:N0} coins", "nl": "{0:N0} munten" }
          }
        }
        """;

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void TextsFallBackFromTheLanguageToItsParentToTheFallbackLanguage()
    {
        var localization = new LocalizationService(new LocalizationOptions("pt-BR", "en"));
        localization.AddTable(StringTable.Parse("strings.tloc", Encoding.UTF8.GetBytes(Strings)));

        Assert.Equal("Começar", localization.Get("menu.start"));
        Assert.Equal("Quit", localization.Get("menu.quit"));
        Assert.Equal("menu.missing", localization.Get("menu.missing"));
        Assert.False(localization.TryGet("menu.missing", out _));
        Assert.Equal(["en", "nl", "pt"], localization.AvailableLanguages);
    }

    [Fact]
    public void FormattedTextsUseTheLanguagesNumberFormat()
    {
        var localization = new LocalizationService(new LocalizationOptions("nl"));
        localization.AddTable(StringTable.Parse("strings.tloc", Encoding.UTF8.GetBytes(Strings)));

        Assert.Equal("1.234 munten", localization.Format("hud.coins", 1234));
        localization.CurrentLanguage = "en";
        Assert.Equal("1,234 coins", localization.Format("hud.coins", 1234));
    }

    [Fact]
    public void ChangingTheLanguageRaisesChanged()
    {
        var localization = new LocalizationService(new LocalizationOptions("en"));
        var raised = 0;
        localization.Changed += (_, _) => raised++;

        localization.CurrentLanguage = "nl";
        localization.CurrentLanguage = "nl";

        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task TablesInTheAssetFolderAreLoadedAndReloaded()
    {
        _folder.Write("text/menu.tloc", Strings);
        var events = new EventBus();
        using var manager = TestAssets.Manager(_folder.AssetRoot, events: events);
        using var localization = new LocalizationService(new LocalizationOptions("en"), manager, events);
        Assert.Equal("Start", localization.Get("menu.start"));

        _folder.Write("text/menu.tloc", Strings.Replace("\"Start\"", "\"Play\"", StringComparison.Ordinal));
        await manager.ReloadAsync("text/menu.tloc", TestContext.Current.CancellationToken);
        events.DispatchQueued();

        Assert.Equal("Play", localization.Get("menu.start"));
    }

    [Fact]
    public void InvalidTablesAreRejected() =>
        Assert.Throws<AssetException>(() => StringTable.Parse("bad.tloc", Encoding.UTF8.GetBytes("""{ "strings": { "a": "not an object" } }""")));
}
