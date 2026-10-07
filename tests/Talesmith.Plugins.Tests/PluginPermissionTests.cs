using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Talesmith.Plugins.Tests;

public sealed class PluginPermissionTests : IDisposable
{
    private const string Downloader = "public static class Downloader { public static System.Net.Http.HttpClient Create() => new(); }";

    private readonly TestPlugins _plugins = new();

    public void Dispose() => _plugins.Dispose();

    [Fact]
    public void WarnsAboutUndeclaredPermissionsTheCodeAndRegistrationsNeed()
    {
        _plugins.Add("net", TestPlugins.PluginSource("Net", "", Downloader));
        _plugins.Add("systems", TestPlugins.PluginSource("Systems", "Talesmith.Systems.SystemServiceCollectionExtensions.AddSystem<Spin>(services);",
            "public sealed class Spin : Talesmith.Systems.ISystem { public void Update(in Talesmith.Systems.SystemContext context) { } }"));
        _plugins.Add("declared", TestPlugins.PluginSource("Declared", "Talesmith.Systems.SystemServiceCollectionExtensions.AddSystem<Spin>(services);",
            "public sealed class Spin : Talesmith.Systems.ISystem { public void Update(in Talesmith.Systems.SystemContext context) { } }\n" + Downloader),
            "\"permissions\": [ \"network\", \"runtimeScene\" ]");

        var report = PluginLoader.Load(_plugins.Options(), new ServiceCollection(), NullLogger.Instance);

        var net = report.Find("net")!;
        Assert.Equal(PluginState.Loaded, net.State);
        Assert.Equal(PluginPermissions.Network, net.UndeclaredPermissions);
        Assert.Contains("its assembly uses System.Net.Http.HttpClient without declaring the \"network\" permission.", net.Warnings);

        var systems = report.Find("systems")!;
        Assert.Equal(PluginPermissions.RuntimeScene, systems.UndeclaredPermissions);
        Assert.Contains("it registers systems (Spin) without declaring the \"runtimeScene\" permission.", systems.Warnings);

        Assert.Empty(report.Find("declared")!.Warnings);
        Assert.Equal(PluginPermissions.None, report.Find("declared")!.UndeclaredPermissions);
    }

    [Fact]
    public void FailsPluginsWithUndeclaredPermissionsWhenEnforcing()
    {
        _plugins.Add("net", TestPlugins.PluginSource("Net", "", Downloader));
        _plugins.Add("systems", TestPlugins.PluginSource("Systems", "Talesmith.Systems.SystemServiceCollectionExtensions.AddSystem<Spin>(services);",
            "public sealed class Spin : Talesmith.Systems.ISystem { public void Update(in Talesmith.Systems.SystemContext context) { } }"));
        var services = new ServiceCollection();

        var report = PluginLoader.Load(_plugins.Options(PluginPermissionPolicy.Enforce), services, NullLogger.Instance);

        Assert.Equal("its code uses undeclared permissions: network.", report.Find("net")!.Reason);
        Assert.Equal("it registers services that need undeclared permissions: runtimeScene.", report.Find("systems")!.Reason);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(Talesmith.Systems.SystemDescriptor));
    }

    [Fact]
    public void AppliesRulesHostsAddForTheirOwnServices()
    {
        _plugins.Add("pass", TestPlugins.PluginSource("Pass", "services.AddSingleton<System.IDisposable, Bloom>();",
            "public sealed class Bloom : System.IDisposable { public void Dispose() { } }"));
        var options = _plugins.Options() with
        {
            RegistrationRules = [.. PluginRegistrationRule.Defaults, PluginRegistrationRule.ForType(typeof(IDisposable), PluginPermissions.RenderBackend, "render passes")]
        };

        var entry = PluginLoader.Load(options, new ServiceCollection(), NullLogger.Instance).Find("pass")!;

        Assert.Contains("it registers render passes (Bloom) without declaring the \"renderBackend\" permission.", entry.Warnings);
    }

    [Fact]
    public void PermissionChecksReportUndeclaredUseOnceAndRefuseOnlyWhenEnforcing()
    {
        var declared = new Dictionary<string, PluginPermissions> { ["a"] = PluginPermissions.Network };
        var warn = new PluginPermissionService(declared, PluginPermissionPolicy.Warn, NullLogger.Instance);
        var enforce = new PluginPermissionService(declared, PluginPermissionPolicy.Enforce, NullLogger.Instance);
        var raised = new List<PluginPermissionViolation>();
        warn.ViolationRecorded += (_, violation) => raised.Add(violation);

        Assert.True(warn.Check("a", PluginPermissions.Network, "download"));
        Assert.True(warn.Check("a", PluginPermissions.Network | PluginPermissions.AssetWrite, "save asset"));
        Assert.True(warn.Check("a", PluginPermissions.AssetWrite, "save asset"));
        Assert.False(enforce.Check("a", PluginPermissions.AssetWrite, "save asset"));
        Assert.False(enforce.Check("unknown", PluginPermissions.Network, "download"));
        Assert.True(enforce.Check(typeof(string), PluginPermissions.ProcessExecution, "engine work"));

        Assert.Equal([new PluginPermissionViolation("a", PluginPermissions.AssetWrite, "save asset", Denied: false)], warn.Violations);
        Assert.Single(raised);
        Assert.Equal(2, enforce.Violations.Count);
        Assert.All(enforce.Violations, v => Assert.True(v.Denied));
    }

    [Fact]
    public void FindsThePluginThatOwnsAType()
    {
        _plugins.Add("owner", TestPlugins.PluginSource("Owner", "services.AddSingleton<Thing>();", "public sealed class Thing { }"), "\"permissions\": [ \"fileSystem\" ]");
        using var manager = new PluginManager(_plugins.Options(), NullLogger.Instance);
        var services = new ServiceCollection();
        manager.Configure(services);
        var permissions = manager.Permissions!;
        var thing = services.Single(d => d.ServiceType.Name == "Thing").ServiceType;

        Assert.Equal("owner", permissions.FindPlugin(thing));
        Assert.Null(permissions.FindPlugin(typeof(PluginManager)));
        Assert.Equal(PluginPermissions.FileSystem, permissions.GetDeclared("owner"));
        Assert.True(permissions.Check(thing, PluginPermissions.FileSystem, "read save game"));
        Assert.True(permissions.Check(thing, PluginPermissions.Network, "upload"));
        Assert.Equal("owner", Assert.Single(permissions.Violations).PluginId);
    }
}
