namespace Talesmith.Plugins.Tests;

public sealed class PluginManifestTests
{
    [Fact]
    public void ReadsAContractVersion1ManifestWithDefaultsForNewFields()
    {
        var manifest = PluginManifest.Parse("""
            {
              "id": "samples.cutscenes",
              "name": "Cutscenes and dialogue",
              "version": "1.2",
              "description": "Plays cutscenes.",
              "authors": [ "Talesmith" ],
              "assembly": "Talesmith.Samples.Cutscenes.dll",
              "contractVersion": 1,
              "dependencies": [ { "id": "samples.hexquest", "minimumVersion": "1.0.0" } ],
              "enabled": true
            }
            """);

        Assert.Equal("samples.cutscenes", manifest.Id);
        Assert.Equal(new Version(1, 2, 0), manifest.Version);
        Assert.Equal(["Talesmith"], manifest.Authors);
        Assert.Equal(PluginPermissions.None, manifest.Permissions);
        Assert.Empty(manifest.Extensions);
        Assert.Null(manifest.EditorAssemblyFile);
        Assert.Null(manifest.MinEngineVersion);
        var dependency = Assert.Single(manifest.Dependencies);
        Assert.Equal(">=1.0.0", dependency.Versions.ToString());
        Assert.False(dependency.Optional);
        Assert.True(dependency.Versions.IsSatisfiedBy(new Version(3, 0)));
        Assert.False(dependency.Versions.IsSatisfiedBy(new Version(0, 9)));
    }

    [Fact]
    public void ReadsEveryFieldOfAFullManifest()
    {
        var manifest = PluginManifest.Parse("""
            {
              "id": "tools.weather",
              "name": "Weather",
              "version": "2.1.3",
              "description": "Rain and snow.",
              "authors": [ "Ada", "Grace" ],
              "license": "MIT",
              "homepage": "https://example.com/weather",
              "icon": "media/icon.png",
              "assembly": "Weather.dll",
              "editorAssembly": "Weather.Editor.dll",
              "contractVersion": 1,
              "minEngineVersion": "0.1",
              "dependencies": [
                { "id": "tools.particles", "version": "^1.2" },
                { "id": "tools.audio", "version": ">=1.0 <2.0", "optional": true }
              ],
              "permissions": [ "runtimeScene", "editorUi", "renderBackend" ],
              "extensions": [ "systems", "particles.modules", "editor.panels" ],
              "assets": "assets/",
              "enabled": false
            }
            """);

        Assert.Equal("Weather", manifest.Name);
        Assert.Equal("MIT", manifest.License);
        Assert.Equal(new Uri("https://example.com/weather"), manifest.Homepage);
        Assert.Equal("media/icon.png", manifest.IconFile);
        Assert.Equal("Weather.Editor.dll", manifest.EditorAssemblyFile);
        Assert.Equal(new Version(0, 1, 0), manifest.MinEngineVersion);
        Assert.Equal(PluginPermissions.RuntimeScene | PluginPermissions.EditorUi | PluginPermissions.RenderBackend, manifest.Permissions);
        Assert.Equal(["systems", "particles.modules", "editor.panels"], manifest.Extensions);
        Assert.Equal("assets", manifest.AssetsFolder);
        Assert.False(manifest.Enabled);
        Assert.Collection(manifest.Dependencies,
            d => Assert.Equal(("tools.particles", "^1.2", false), (d.Id, d.Versions.ToString(), d.Optional)),
            d => Assert.Equal(("tools.audio", ">=1.0 <2.0", true), (d.Id, d.Versions.ToString(), d.Optional)));
    }

    [Fact]
    public void ListsEveryProblemWithSuggestionsForTypos()
    {
        var error = Assert.Throws<InvalidDataException>(() => PluginManifest.Parse("""
            {
              "id": "Bad Id",
              "assemby": "Weather.dll",
              "editorAssembly": "Weather.Editor.exe",
              "homepage": "ftp://example.com",
              "assets": "../shared",
              "permissions": [ "netwrk", "fileSystem", "fileSystem" ],
              "extensions": [ "Editor Panels" ],
              "dependencies": [
                { "id": "a", "version": "banana" },
                { "id": "b", "version": "1.0", "minimumVersion": "1.0" },
                { "id": "c", "optinal": true }
              ]
            }
            """, "weather/plugin.json"));

        var message = error.Message;
        Assert.StartsWith("weather/plugin.json is invalid:", message, StringComparison.Ordinal);
        Assert.Contains("id \"Bad Id\" must be lowercase", message, StringComparison.Ordinal);
        Assert.Contains("unknown property \"assemby\"; did you mean \"assembly\"?", message, StringComparison.Ordinal);
        Assert.Contains("assembly is required.", message, StringComparison.Ordinal);
        Assert.Contains("contractVersion is required", message, StringComparison.Ordinal);
        Assert.Contains("editorAssembly \"Weather.Editor.exe\" must be the file name of a .dll", message, StringComparison.Ordinal);
        Assert.Contains("homepage \"ftp://example.com\" must be an http or https address", message, StringComparison.Ordinal);
        Assert.Contains("assets \"../shared\" must be a folder inside the plugin folder", message, StringComparison.Ordinal);
        Assert.Contains("permission \"netwrk\" is unknown; did you mean \"network\"?", message, StringComparison.Ordinal);
        Assert.Contains("permission \"fileSystem\" is listed more than once.", message, StringComparison.Ordinal);
        Assert.Contains("extension \"Editor Panels\" must be lowercase", message, StringComparison.Ordinal);
        Assert.Contains("dependencies[0] version: \"banana\"", message, StringComparison.Ordinal);
        Assert.Contains("dependencies[1] has both version and minimumVersion", message, StringComparison.Ordinal);
        Assert.Contains("dependencies[2] has unknown property \"optinal\"; did you mean \"optional\"?", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSelfAndRepeatedDependenciesAndAnEditorAssemblyEqualToTheAssembly()
    {
        var error = Assert.Throws<InvalidDataException>(() => PluginManifest.Parse("""
            {
              "id": "a", "assembly": "A.dll", "editorAssembly": "a.dll", "contractVersion": 1,
              "dependencies": [ { "id": "a" }, { "id": "b" }, { "id": "b" } ]
            }
            """));

        Assert.Contains("a plugin cannot depend on itself.", error.Message, StringComparison.Ordinal);
        Assert.Contains("dependency \"b\" is listed more than once.", error.Message, StringComparison.Ordinal);
        Assert.Contains("editorAssembly must be a separate assembly", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsInvalidJsonWithTheSource()
    {
        var error = Assert.Throws<InvalidDataException>(() => PluginManifest.Parse("{ \"id\": ", "x/plugin.json"));
        Assert.StartsWith("x/plugin.json is not valid JSON", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesExtensionPointsAndPermissionsForDisplay()
    {
        Assert.Equal("Editor panels", PluginExtensionPoints.GetDisplayName(PluginExtensionPoints.EditorPanels));
        Assert.Equal("custom.thing", PluginExtensionPoints.GetDisplayName("custom.thing"));
        Assert.Equal("network, editorUi", PluginPermissionNames.GetName(PluginPermissions.Network | PluginPermissions.EditorUi));
        Assert.Equal("Asset write access", PluginPermissionNames.GetDisplayName(PluginPermissions.AssetWrite));
    }
}
