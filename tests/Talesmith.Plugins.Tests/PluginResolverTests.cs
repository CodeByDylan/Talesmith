namespace Talesmith.Plugins.Tests;

public sealed class PluginResolverTests
{
    private static readonly Version Engine = new(0, 1, 0);

    [Fact]
    public void OrdersDependenciesFirstAndBreaksTiesById()
    {
        var resolution = Resolve(
            Plugin("c", Requires("a")),
            Plugin("z"),
            Plugin("b"),
            Plugin("a", Requires("z")));

        Assert.Equal(["b", "z", "a", "c"], resolution.LoadOrder.Select(p => p.Id));
        Assert.All(resolution.Entries, e => Assert.Equal(PluginState.Pending, e.State));
    }

    [Fact]
    public void SkipsPluginsWhoseRequiredDependencyIsMissingAndEverythingThatNeedsThem()
    {
        var resolution = Resolve(Plugin("a", Requires("ghost", ">=1.2")), Plugin("b", Requires("a")));

        Assert.Empty(resolution.LoadOrder);
        Assert.Equal("it requires ghost >=1.2, which is not installed.", Entry(resolution, "a").Reason);
        Assert.Equal(PluginState.Skipped, Entry(resolution, "b").State);
        Assert.Equal("it requires a, which is skipped.", Entry(resolution, "b").Reason);
    }

    [Fact]
    public void SkipsDependentsOfDisabledPlugins()
    {
        var configuration = PluginConfiguration.Default.WithPluginEnabled("a", enabled: false);
        var resolution = Resolve(configuration, Plugin("a"), Plugin("b", Requires("a")), Plugin("c"));

        Assert.Equal(["c"], resolution.LoadOrder.Select(p => p.Id));
        Assert.Equal(PluginState.Disabled, Entry(resolution, "a").State);
        Assert.Equal("it requires a, which is disabled.", Entry(resolution, "b").Reason);
    }

    [Fact]
    public void ChecksDependencyVersionRanges()
    {
        var resolution = Resolve(Custom("a", version: "1.4.0"), Plugin("b", Requires("a", "^2.0")), Plugin("c", Requires("a", "~1.4")));

        Assert.Equal(["a", "c"], resolution.LoadOrder.Select(p => p.Id));
        Assert.Equal("it requires a ^2.0, but version 1.4.0 is installed.", Entry(resolution, "b").Reason);
    }

    [Fact]
    public void LoadsWithoutMissingOrMismatchedOptionalDependenciesButAfterPresentOnes()
    {
        var resolution = Resolve(
            Plugin("a", Optional("z"), Optional("ghost"), Optional("old", ">=2.0")),
            Plugin("z"),
            Plugin("old"));

        Assert.Equal(["old", "z", "a"], resolution.LoadOrder.Select(p => p.Id));
        var a = resolution.LoadOrder.Single(p => p.Id == "a");
        Assert.Equal(["z"], a.Dependencies);
        Assert.Contains("it optionally uses ghost, which is not installed; it loads without it.", a.Warnings);
        Assert.Contains("it optionally uses old >=2.0, but version 1.0.0 is installed; it loads without it.", a.Warnings);
    }

    [Fact]
    public void DetectsDependencyCyclesAndSkipsTheirDependents()
    {
        var resolution = Resolve(Plugin("a", Requires("b")), Plugin("b", Requires("a")), Plugin("c", Requires("a")), Plugin("d"));

        Assert.Equal(["d"], resolution.LoadOrder.Select(p => p.Id));
        Assert.Equal("its dependencies form a cycle: a -> b -> a.", Entry(resolution, "a").Reason);
        Assert.Equal("its dependencies form a cycle: b -> a -> b.", Entry(resolution, "b").Reason);
        Assert.Equal("it depends on a, whose dependencies form a cycle.", Entry(resolution, "c").Reason);
    }

    [Fact]
    public void FailsEveryCopyOfADuplicatedId()
    {
        var resolution = PluginResolver.Resolve(
            [new PluginCandidate("/plugins/one", Manifest("a")), new PluginCandidate("/plugins/two", Manifest("a")), new PluginCandidate("/plugins/b", Manifest("b", Requires("a")))],
            [], PluginConfiguration.Default, new HashSet<string>(), Engine, EngineInfo.ContractVersion);

        var copies = resolution.Entries.Where(e => e.Id == "a").ToList();
        Assert.Equal(2, copies.Count);
        Assert.All(copies, c => Assert.Equal(PluginState.Failed, c.State));
        Assert.Contains("used by more than one plugin folder (one, two)", copies[0].Reason, StringComparison.Ordinal);
        Assert.Equal("it requires a, which is installed more than once.", Entry(resolution, "b").Reason);
    }

    [Fact]
    public void SkipsPluginsBuiltForAnotherEngine()
    {
        var resolution = Resolve(Custom("new", extra: "\"minEngineVersion\": \"9.0\""), Custom("old", contract: 99), Custom("ok", extra: "\"minEngineVersion\": \"0.1\""));

        Assert.Equal(["ok"], resolution.LoadOrder.Select(p => p.Id));
        Assert.Equal("it needs engine version 9.0.0 or newer, but this engine is version 0.1.0.", Entry(resolution, "new").Reason);
        Assert.StartsWith("it was built for engine contract version 99", Entry(resolution, "old").Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AppliesConfigurationSwitchesOverTheManifestAndHostSwitchesOverBoth()
    {
        var configuration = PluginConfiguration.Default.WithPluginEnabled("off-in-manifest", enabled: true).WithPluginEnabled("host-off", enabled: true);
        var resolution = PluginResolver.Resolve(
            [Candidate(Custom("off-in-manifest", extra: "\"enabled\": false")), Candidate(Custom("plain-off", extra: "\"enabled\": false")), Candidate(Plugin("host-off"))],
            [], configuration, new HashSet<string> { "host-off" }, Engine, EngineInfo.ContractVersion);

        Assert.Equal(["off-in-manifest"], resolution.LoadOrder.Select(p => p.Id));
        Assert.StartsWith("disabled in its manifest", Entry(resolution, "plain-off").Reason, StringComparison.Ordinal);
        Assert.Equal("disabled by the host.", Entry(resolution, "host-off").Reason);
    }

    [Fact]
    public void ListsLoadablePluginsFirstThenTheRestByIdThenUnreadableFolders()
    {
        var unreadable = new PluginLoadEntry { Directory = "/plugins/broken", State = PluginState.Failed, Reason = "bad" };
        var resolution = PluginResolver.Resolve(
            [Candidate(Plugin("b", Requires("ghost"))), Candidate(Plugin("c")), Candidate(Plugin("a", Requires("ghost")))],
            [unreadable], PluginConfiguration.Default, new HashSet<string>(), Engine, EngineInfo.ContractVersion);

        Assert.Equal(["c", "a", "b", null], resolution.Entries.Select(e => e.Id));
    }

    private static PluginResolution Resolve(params string[] manifests) => Resolve(PluginConfiguration.Default, manifests);

    private static PluginResolution Resolve(PluginConfiguration configuration, params string[] manifests) =>
        PluginResolver.Resolve([.. manifests.Select(Candidate)], [], configuration, new HashSet<string>(), Engine, EngineInfo.ContractVersion);

    private static PluginCandidate Candidate(string manifest)
    {
        var parsed = PluginManifest.Parse(manifest);
        return new PluginCandidate($"/plugins/{parsed.Id}", parsed);
    }

    private static PluginLoadEntry Entry(PluginResolution resolution, string id) => resolution.Entries.Single(e => e.Id == id);

    private static string Requires(string id, string range = "*") => $$"""{ "id": "{{id}}", "version": "{{range}}" }""";

    private static string Optional(string id, string range = "*") => $$"""{ "id": "{{id}}", "version": "{{range}}", "optional": true }""";

    private static string Plugin(string id, params string[] dependencies) => Custom(id, dependencies: dependencies);

    private static string Custom(string id, string version = "1.0.0", int contract = 1, string extra = "", string[]? dependencies = null) =>
        $$"""{ "id": "{{id}}", "version": "{{version}}", "assembly": "{{id}}.dll", "contractVersion": {{contract}}, "dependencies": [{{string.Join(", ", dependencies ?? [])}}]{{(extra.Length > 0 ? ", " + extra : "")}} }""";

    private static PluginManifest Manifest(string id, params string[] dependencies) => PluginManifest.Parse(Plugin(id, dependencies));
}
