using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Talesmith.Plugins.Tests;

public sealed class PluginLoaderTests : IDisposable
{
    private readonly TestPlugins _plugins = new();

    public void Dispose() => _plugins.Dispose();

    [Fact]
    public void ConfiguresPluginsAndRegistersTheReportPermissionsAndSettings()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", """
            builder.Settings.Set("configured", true);
            services.AddSingleton(new Marker(builder.Plugin.Id));
            """, "public sealed record Marker(string Id);"));
        var services = new ServiceCollection();

        var report = PluginLoader.Load(_plugins.Options(), services, NullLogger.Instance);

        var entry = Assert.Single(report.Loaded);
        Assert.Equal("alpha", entry.Id);
        Assert.Empty(entry.Warnings);
        Assert.Contains(services, d => d.ServiceType.Name == "Marker");
        Assert.Contains(services, d => d.ServiceType == typeof(PluginLoadReport));
        Assert.Contains(services, d => d.ServiceType == typeof(IPluginPermissions));
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredKeyedService<IPluginSettings>("alpha").Get("configured", false));
    }

    [Fact]
    public void KeepsNoRegistrationsOfAPluginThatThrowsAndSkipsPluginsThatRequireIt()
    {
        _plugins.Add("broken", TestPlugins.PluginSource("Broken", """
            services.AddSingleton(new object());
            throw new System.InvalidOperationException("boom");
            """));
        _plugins.Add("dependent", TestPlugins.PluginSource("Dependent", ""), "\"dependencies\": [ { \"id\": \"broken\" } ]");
        _plugins.Add("helper", TestPlugins.PluginSource("Helper", ""), "\"dependencies\": [ { \"id\": \"broken\", \"optional\": true } ]");
        var services = new ServiceCollection();

        var report = PluginLoader.Load(_plugins.Options(), services, NullLogger.Instance);

        var broken = report.Find("broken")!;
        Assert.Equal(PluginState.Failed, broken.State);
        Assert.Contains("threw InvalidOperationException: boom", broken.Reason, StringComparison.Ordinal);
        Assert.Contains("boom", broken.ErrorDetails, StringComparison.Ordinal);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(object));
        Assert.Equal("it requires broken, which failed to load.", report.Find("dependent")!.Reason);
        Assert.Equal(PluginState.Loaded, report.Find("helper")!.State);
        Assert.Contains("it optionally uses broken, which failed to load; it is configured without it.", report.Find("helper")!.Warnings);
    }

    [Fact]
    public void FailsAssembliesWithoutExactlyOnePluginClass()
    {
        _plugins.Add("none", "namespace None; public sealed class Nothing { }");
        _plugins.Add("twice", TestPlugins.PluginSource("Twice", "", "public sealed class Second : IPlugin { public void Configure(IPluginBuilder builder) { } }"));
        _plugins.AddManifest("missing", TestPlugins.Manifest("missing"));

        var report = PluginLoader.Load(_plugins.Options(), new ServiceCollection(), NullLogger.Instance);

        Assert.Contains("has no public, non-abstract class implementing IPlugin", report.Find("none")!.Reason, StringComparison.Ordinal);
        Assert.Contains("has more than one IPlugin class", report.Find("twice")!.Reason, StringComparison.Ordinal);
        Assert.Contains("was not found", report.Find("missing")!.Reason, StringComparison.Ordinal);
        Assert.Equal(3, report.Failed.Count);
    }

    [Fact]
    public void SharesTypesWithDependenciesSoPluginsCanUseEachOther()
    {
        var core = _plugins.Add("core", TestPlugins.PluginSource("Core", "services.AddSingleton<Greeter>();", "public sealed class Greeter { public string Hello => \"hi\"; }"));
        _plugins.Add("user", TestPlugins.PluginSource("User", "services.AddSingleton(new Core.Greeter());"), "\"dependencies\": [ { \"id\": \"core\", \"version\": \"^1.0\" } ]", core);
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);
        var services = new ServiceCollection();

        var report = manager.Configure(services);

        Assert.Equal(["core", "user"], report.Loaded.Select(p => p.Id));
        var greeters = services.Where(d => d.ServiceType.Name == "Greeter").ToList();
        Assert.Equal(2, greeters.Count);
        Assert.Same(greeters[0].ServiceType, greeters[1].ServiceType);
        Assert.Same(manager.Assemblies.Single(a => a.Plugin.Id == "core").Assembly, greeters[0].ServiceType.Assembly);
    }

    [Fact]
    public void LoadsEditorAssembliesIntoThePluginContextOnlyWhenAsked()
    {
        var runtime = _plugins.Add("tool", TestPlugins.PluginSource("Tool", "", "public sealed class Brush { }"), "\"editorAssembly\": \"Tool.Editor.dll\", \"permissions\": [ \"editorUi\" ]");
        _plugins.AddEditorAssembly("tool", "Tool.Editor.dll", "namespace Tool.Editor; public sealed class BrushPanel { public Tool.Brush Brush { get; } = new(); }", runtime);

        using (var game = new PluginManager(_plugins.Options(), NullLogger.Instance))
        {
            var report = game.Configure(new ServiceCollection());
            Assert.False(report.Find("tool")!.EditorAssemblyLoaded);
            Assert.Null(game.Assemblies.Single().EditorAssembly);
        }

        using var editor = new PluginManager(_plugins.Options(editor: true), NullLogger.Instance);
        var editorReport = editor.Configure(new ServiceCollection());
        var assemblies = editor.Assemblies.Single();

        Assert.True(editorReport.Find("tool")!.EditorAssemblyLoaded);
        Assert.NotNull(assemblies.EditorAssembly);
        Assert.Same(AssemblyLoadContext.GetLoadContext(assemblies.Assembly), AssemblyLoadContext.GetLoadContext(assemblies.EditorAssembly));
        var panel = assemblies.EditorAssembly.GetType("Tool.Editor.BrushPanel", throwOnError: true)!;
        Assert.Same(assemblies.Assembly.GetType("Tool.Brush"), panel.GetProperty("Brush")!.PropertyType);
    }

    [Fact]
    public void LoadsAssembliesIntoMemoryWhenAskedSoTheirFilesCanBeRebuilt()
    {
        _plugins.Add("alpha", TestPlugins.PluginSource("Alpha", ""));
        using var manager = new PluginManager(_plugins.Options() with { LoadInMemory = true }, NullLogger.Instance);

        manager.Configure(new ServiceCollection());

        Assert.Equal("", manager.Assemblies.Single().Assembly.Location);
        File.WriteAllBytes(Path.Combine(_plugins.PluginsDirectory, "alpha", TestPlugins.AssemblyName("alpha") + ".dll"), [0]);
    }

    [Fact]
    public void ScansReportFolderProblemsWithoutLoading()
    {
        _plugins.AddManifest("art", TestPlugins.Manifest("art", "\"assets\": \"assets\", \"icon\": \"icon.png\""));
        _plugins.AddManifest("broken", "{ \"id\": 3 }");
        _plugins.AddManifest("empty", "{}");
        File.Delete(Path.Combine(_plugins.PluginsDirectory, "empty", PluginManifest.FileName));

        var scan = PluginLoader.Scan(_plugins.Options(), PluginConfiguration.Default);

        var art = scan.Find("art")!;
        Assert.Equal(PluginState.Pending, art.State);
        Assert.Contains("its assets folder \"assets\" does not exist.", art.Warnings);
        Assert.Contains("its icon \"icon.png\" does not exist.", art.Warnings);
        Assert.Equal(Path.Combine(_plugins.PluginsDirectory, "art", "assets"), art.Info!.AssetsDirectory);
        Assert.Contains(scan.Failed, f => f.Directory.EndsWith("broken", StringComparison.Ordinal) && f.Reason!.Contains("id must be a string", StringComparison.Ordinal));
        Assert.Contains(scan.Failed, f => f.Directory.EndsWith("empty", StringComparison.Ordinal) && f.Reason == "the folder has no plugin.json.");
        Assert.Contains("failed", scan.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void WarnsAboutAMissingEditorAssemblyWithoutFailingThePlugin()
    {
        _plugins.Add("tool", TestPlugins.PluginSource("Tool", ""), "\"editorAssembly\": \"Tool.Editor.dll\"");
        using var editor = new PluginManager(_plugins.Options(editor: true), NullLogger.Instance);

        var entry = editor.Configure(new ServiceCollection()).Find("tool")!;

        Assert.Equal(PluginState.Loaded, entry.State);
        Assert.Contains("it ships an editor assembly (Tool.Editor.dll) but does not declare the \"editorUi\" permission.", entry.Warnings);
        Assert.Contains("its editor assembly Tool.Editor.dll was not found, so its editor features are unavailable.", entry.Warnings);
    }
}
