using System.Text;
using System.Text.Json;

namespace Talesmith.Plugins.Tests;

public sealed class PluginConfigurationTests
{
    [Fact]
    public void ReadsTheOriginalDisabledOnlyFormat()
    {
        var configuration = Parse("""{ "disabled": [ "samples.cutscenes" ] }""");

        Assert.False(configuration.IsEnabled("samples.cutscenes"));
        Assert.Null(configuration.IsEnabled("samples.hexquest"));
    }

    [Fact]
    public void RoundTripsSwitchesAndSettingsThroughAFile()
    {
        using var folder = new TestPlugins();
        using var settings = JsonDocument.Parse("""{ "difficulty": "hard" }""");
        var configuration = PluginConfiguration.Default
            .WithPluginEnabled("b", enabled: false)
            .WithPluginEnabled("a", enabled: true)
            .WithSettings("a", settings.RootElement);

        configuration.Save(folder.ConfigurationFile);
        var loaded = PluginConfiguration.Load(folder.ConfigurationFile);

        Assert.True(loaded.IsEnabled("a"));
        Assert.False(loaded.IsEnabled("b"));
        Assert.Equal("hard", loaded.Settings["a"].GetProperty("difficulty").GetString());
        Assert.Equal(["plugins.json"], Directory.GetFiles(Path.GetDirectoryName(folder.ConfigurationFile)!).Select(Path.GetFileName));
    }

    [Fact]
    public void SwitchingAPluginOnRemovesItFromTheDisabledList()
    {
        var configuration = PluginConfiguration.Default.WithPluginEnabled("a", enabled: false).WithPluginEnabled("a", enabled: true);

        Assert.Empty(configuration.Disabled);
        Assert.True(configuration.IsEnabled("a"));
    }

    [Theory]
    [InlineData("""{ "disabeld": [] }""", "unknown property \"disabeld\"")]
    [InlineData("""{ "disabled": "a" }""", "disabled must be a list of plugin ids")]
    [InlineData("""{ "enabled": [ "a" ], "disabled": [ "a" ] }""", "lists a as both enabled and disabled")]
    [InlineData("""{ "settings": { "a": 3 } }""", "settings of \"a\" must be a JSON object")]
    [InlineData("""[]""", "must contain a JSON object")]
    public void RejectsMalformedConfiguration(string json, string expected)
    {
        var error = Assert.Throws<InvalidDataException>(() => Parse(json));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsAreTypedJsonValuesSavedIntoTheConfigurationFile()
    {
        using var folder = new TestPlugins();
        var store = PluginConfigurationStore.Open(folder.ConfigurationFile);
        var settings = new PluginSettings("weather", store);

        settings.Set("intensity", 0.75f);
        settings.Set("palette", new Palette("Storm", [1, 2, 3]));
        settings.Set("label", (string?)null);
        settings.Save();

        var reopened = new PluginSettings("weather", PluginConfigurationStore.Open(folder.ConfigurationFile));
        Assert.Equal(0.75f, reopened.Get("intensity", 0f));
        Assert.Equal("Storm", reopened.Get<Palette?>("palette", null)!.Name);
        Assert.True(reopened.TryGet<string?>("label", out var label));
        Assert.Null(label);
        Assert.False(reopened.TryGet<int>("label", out _));
        Assert.False(reopened.TryGet<int>("palette", out _));
        Assert.Equal(42, reopened.Get("missing", 42));
        Assert.Equal(["intensity", "label", "palette"], reopened.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("\"intensity\": 0.75", File.ReadAllText(folder.ConfigurationFile), StringComparison.Ordinal);
    }

    [Fact]
    public void SavingSettingsKeepsSwitchesAndOtherPluginsUnsavedChangesInMemory()
    {
        using var folder = new TestPlugins();
        var store = PluginConfigurationStore.Open(folder.ConfigurationFile);
        var first = new PluginSettings("first", store);
        var second = new PluginSettings("second", store);

        store.SetEnabled("second", enabled: false);
        first.Set("value", 1);
        second.Set("value", 2);
        first.Save();
        store.Refresh();

        var saved = PluginConfiguration.Load(folder.ConfigurationFile);
        Assert.False(saved.IsEnabled("second"));
        Assert.True(saved.Settings.ContainsKey("first"));
        Assert.False(saved.Settings.ContainsKey("second"));
        Assert.Equal(2, second.Get("value", 0));
    }

    [Fact]
    public void InMemorySettingsNeverTouchTheDisk()
    {
        var settings = new PluginSettings("memory");
        settings.Set("a", 1);
        settings.Save();

        Assert.True(settings.Contains("a"));
        Assert.True(settings.Remove("a"));
        Assert.False(settings.Remove("a"));
    }

    private static PluginConfiguration Parse(string json) => PluginConfiguration.Parse(Encoding.UTF8.GetBytes(json), "plugins.json");

    private sealed record Palette(string Name, int[] Colors);
}
