using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Talesmith.Plugins.Tests;

public sealed class PluginManagerTests : IDisposable
{
    private readonly TestPlugins _plugins = new();

    public void Dispose() => _plugins.Dispose();

    [Fact]
    public void ScansWithoutLoadingAnything()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", ""));
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);

        Assert.Equal(PluginState.Pending, manager.Scan.Find("alpha")!.State);
        Assert.False(manager.IsLoaded);
        Assert.False(manager.RestartRequired);
        Assert.Same(PluginLoadReport.Empty, manager.Report);
    }

    [Fact]
    public void PersistsEnableAndDisableAndAsksForARestartWhenLoadedPluginsChange()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", ""));
        _plugins.Add("beta", TestPlugins.PluginSource("Beta", ""), "\"enabled\": false");
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);
        manager.Configure(new ServiceCollection());

        var disabled = manager.Disable("alpha");
        Assert.True(disabled.RestartRequired);
        Assert.Equal(PluginState.Disabled, disabled.Scan.Find("alpha")!.State);
        Assert.Equal(PluginState.Loaded, manager.Report.Find("alpha")!.State);

        var enabled = manager.Enable("beta");
        Assert.Equal(PluginState.Pending, enabled.Scan.Find("beta")!.State);

        var saved = PluginConfiguration.Load(_plugins.ConfigurationFile);
        Assert.False(saved.IsEnabled("alpha"));
        Assert.True(saved.IsEnabled("beta"));

        manager.Unload();
        var report = manager.Configure(new ServiceCollection());
        Assert.Equal(["beta"], report.Loaded.Select(p => p.Id));
        Assert.Equal(PluginState.Disabled, report.Find("alpha")!.State);
        Assert.False(manager.RestartRequired);
        Assert.Throws<ArgumentException>(() => manager.Enable("ghost"));
    }

    [Fact]
    public void ChangesThatDoNotAffectLoadedPluginsNeedNoRestart()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", ""));
        _plugins.Add("orphan", TestPlugins.PluginSource("Orphan", ""), "\"dependencies\": [ { \"id\": \"ghost\" } ]");
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);
        manager.Configure(new ServiceCollection());

        var change = manager.Disable("orphan");

        Assert.False(change.RestartRequired);
    }

    [Fact]
    public void NoticesRebuiltPluginsAndReloadRequests()
    {
        var path = Path.Combine(_plugins.PluginsDirectory, "alpha", TestPlugins.AssemblyName("alpha") + ".dll");
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", ""));
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);
        manager.Configure(new ServiceCollection());

        Assert.True(manager.RequestReload().RestartRequired);
        manager.Unload();
        manager.Configure(new ServiceCollection());
        Assert.False(manager.RestartRequired);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        manager.Rescan();
        Assert.True(manager.RestartRequired);
    }

    [Fact]
    public void SharesLoadedPluginsBetweenGamesUntilUnloadedAndRaisesEvents()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", "services.AddSingleton<Thing>();", "public sealed class Thing { }"));
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);
        var loaded = new List<string>();
        var unloaded = new List<string>();
        manager.PluginLoaded += (_, e) => loaded.Add(e.Plugin.Id);
        manager.PluginUnloaded += (_, e) => unloaded.Add(e.Plugin.Id);
        var edit = new ServiceCollection();
        var play = new ServiceCollection();

        manager.Configure(edit);
        manager.Configure(play);

        Assert.Same(edit.Single(d => d.ServiceType.Name == "Thing").ServiceType, play.Single(d => d.ServiceType.Name == "Thing").ServiceType);
        Assert.Equal(["alpha"], loaded);
        manager.Unload();
        Assert.Equal(["alpha"], unloaded);
        Assert.False(manager.IsLoaded);
    }

    [Fact]
    public void UnloadedPluginsAreReleasedFromMemory()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", """
            builder.Settings.Set("thing", new Thing { Size = 3 });
            services.AddSingleton<Thing>();
            """, "public sealed class Thing { public int Size { get; set; } }"));
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);

        var context = RunGameAndUnload(manager);

        Assert.True(context.Unload.WaitForUnload(TimeSpan.FromSeconds(10)), $"Still loaded: {string.Join(", ", context.Unload.StillLoaded)}");
        Assert.False(context.Context.IsAlive);
        Assert.Equal(["alpha"], context.Unload.PluginIds);
    }

    [Fact]
    public void PluginsWhoseComponentsWereUsedInAWorldAreReleasedFromMemory()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", "services.AddSingleton<Thing>();", """
            public struct Marker { public int Value; }

            public sealed class Thing
            {
                public Thing()
                {
                    var world = new Talesmith.Ecs.World();
                    world.Create(new Marker { Value = 1 });
                    world.Query<Marker>().ForEach((Talesmith.Ecs.Entity entity, ref Marker marker) => marker.Value++);
                }
            }
            """));
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);

        var context = RunGameAndUnload(manager);

        Assert.True(context.Unload.WaitForUnload(TimeSpan.FromSeconds(10)), $"Still loaded: {string.Join(", ", context.Unload.StillLoaded)}");
        Assert.False(context.Context.IsAlive);
    }

    [Fact]
    public void NonCollectiblePluginsReportThatTheyStayLoaded()
    {
        var result = new PluginUnloadResult([("alpha", new WeakReference(new object()))], collectible: false);

        Assert.False(result.WaitForUnload(TimeSpan.Zero));
        Assert.Equal(["alpha"], result.StillLoaded);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (PluginUnloadResult Unload, WeakReference Context) RunGameAndUnload(PluginManager manager)
    {
        var services = new ServiceCollection();
        manager.Configure(services);
        var assembly = manager.Assemblies.Single().Assembly;
        var context = new WeakReference(System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(assembly));
        using (var provider = services.BuildServiceProvider())
        {
            var thing = provider.GetRequiredService(assembly.GetType("Alpha.Thing", throwOnError: true)!);
            Assert.NotNull(thing);
        }

        services.Clear();
        return (manager.Unload(), context);
    }
}
