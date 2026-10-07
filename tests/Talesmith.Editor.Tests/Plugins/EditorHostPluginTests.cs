using Talesmith.Plugins;

namespace Talesmith.Editor.Tests.Plugins;

public sealed class EditorHostPluginTests
{
    [Fact]
    public void TheEditorLoadsPluginsInMemorySoTheirFilesCanBeReplacedWhileLoaded() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => TestPlugins.Write(folder, "fixture.weather", "Weather"));
        var manager = editor.Get<PluginManager>();

        var loaded = Assert.Single(manager.Assemblies);
        Assert.True(manager.Options.LoadInMemory);
        Assert.Equal("", loaded.Assembly.Location);
        var file = Assert.Single(Directory.GetFiles(loaded.Plugin.Directory, "*.dll"));
        await File.WriteAllBytesAsync(file, await File.ReadAllBytesAsync(file));
    });
}
