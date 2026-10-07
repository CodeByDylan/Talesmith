using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Events;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Tests;

public sealed class GameSessionEventTests
{
    [Fact]
    public async Task TheGameHearsItsRenderBackendAndPluginsOnItsFirstFrameAndThePluginsGoingAwayWhenTheSessionEnds()
    {
        var assets = SampleAssets("HexQuest");
        Assert.SkipUnless(File.Exists(Path.Combine(assets, "plugins", "cutscenes", "Talesmith.Samples.Cutscenes.dll")), "The sample plugins are not built.");
        var session = GameSession.Create(new DesktopGameOptions
        {
            AssetRoot = assets,
            Renderer = RendererPreference.Skia,
            Audio = false,
            DeveloperTools = false,
            Threading = GameThreading.Host
        }, NullLoggerFactory.Instance, WindowGraphics.None);
        var events = session.Game.Services.GetRequiredService<IEventBus>();
        var backends = new List<RenderBackendChanged>();
        var loaded = new List<string>();
        var unloaded = new List<string>();
        events.Subscribe((ref RenderBackendChanged e) => backends.Add(e));
        events.Subscribe((ref PluginLoaded e) => loaded.Add(e.Plugin.Id));
        events.Subscribe((ref PluginUnloaded e) => unloaded.Add(e.Plugin.Id));

        session.Game.Tick(1.0 / 60);
        session.Game.Tick(1.0 / 60);

        var backend = Assert.Single(backends);
        Assert.Null(backend.Previous);
        Assert.Equal(session.Backend.Renderer.Info, backend.Current);
        Assert.False(string.IsNullOrWhiteSpace(backend.Reason));
        Assert.Equal(["samples.cutscenes", "samples.hexquest"], loaded.Order(StringComparer.Ordinal));
        Assert.Empty(unloaded);

        await session.DisposeAsync();

        Assert.Equal(["samples.cutscenes", "samples.hexquest"], unloaded.Order(StringComparer.Ordinal));
    }

    private static string SampleAssets(string game)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Talesmith.slnx")))
                return Path.Combine(folder.FullName, "samples", game, "assets");
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
