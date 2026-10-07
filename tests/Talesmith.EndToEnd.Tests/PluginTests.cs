using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Plugins;

namespace Talesmith.EndToEnd.Tests;

/// <summary>Switches the Hex Quest sample's cutscene plugin off and on again through the plugin manager, as the Plugins panel does.</summary>
public sealed class PluginTests : IDisposable
{
    private const string Cutscenes = "samples.cutscenes";
    private const string Gameplay = "samples.hexquest";

    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task TheCutscenePluginLoadsOnlyWhileItIsEnabled()
    {
        var assets = Path.Combine(_workspace.CopySample("HexQuest"), "assets");
        Assert.SkipUnless(File.Exists(Path.Combine(assets, "plugins", "cutscenes", "Talesmith.Samples.Cutscenes.dll")), "The sample plugins are not built.");
        using var loggers = new RecordingLoggers();
        using var manager = new PluginManager(new PluginLoadOptions
        {
            PluginsDirectory = Path.Combine(assets, "plugins"),
            ConfigurationFile = Path.Combine(assets, "config", "plugins.json")
        }, loggers.Factory.CreateLogger("Talesmith.Plugins"));

        await using (var game = await HeadlessGame.StartAsync(assets, loggers.Factory, plugins: manager))
        {
            Assert.Equal([Cutscenes, Gameplay], manager.Report.Loaded.Select(p => p.Id).Order(StringComparer.Ordinal));
            game.Run(60);
            Assert.False(game.Find("Hero").IsNull);
            Assert.True(ImportsCutscenes(game));
        }

        var disabled = manager.Disable(Cutscenes);
        Assert.True(disabled.RestartRequired);
        Assert.Equal(PluginState.Disabled, disabled.Scan.Find(Cutscenes)!.State);
        Assert.Contains(Cutscenes, await File.ReadAllTextAsync(Path.Combine(assets, "config", "plugins.json"), TestContext.Current.CancellationToken));
        manager.Unload();

        await using (var game = await HeadlessGame.StartAsync(assets, loggers.Factory, plugins: manager))
        {
            Assert.Equal([Gameplay], manager.Report.Loaded.Select(p => p.Id));
            Assert.Equal(PluginState.Disabled, manager.Report.Find(Cutscenes)!.State);
            game.Run(60);
            Assert.False(game.Find("Hero").IsNull);
            Assert.False(ImportsCutscenes(game));
        }

        Assert.Equal(PluginState.Pending, manager.Enable(Cutscenes).Scan.Find(Cutscenes)!.State);
        manager.Unload();

        await using (var game = await HeadlessGame.StartAsync(assets, loggers.Factory, plugins: manager))
        {
            Assert.Equal(PluginState.Loaded, manager.Report.Find(Cutscenes)!.State);
            Assert.True(ImportsCutscenes(game));
        }

        Assert.DoesNotContain(loggers.Problems, p => p.Contains("Error", StringComparison.Ordinal));
    }

    private static bool ImportsCutscenes(HeadlessGame game) =>
        game.Game.Services.GetServices<IAssetImporter>().Any(i => i.Extensions.Contains(".cutscene", StringComparer.OrdinalIgnoreCase));
}
