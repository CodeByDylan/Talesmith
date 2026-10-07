using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Avalonia.Hosting;
using Talesmith.Avalonia.Presentation;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Editor.Tests.Projects;

public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "talesmith-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectCreator _creator = new([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData("empty", 2, false)]
    [InlineData("hex-adventure", 3, true)]
    [InlineData("platformer", 9, true)]
    public async Task TemplatesCreateProjectsWhoseStartSceneLoads(string templateId, int entities, bool hasMap)
    {
        var folder = await _creator.CreateAsync(_creator.Templates.Single(t => t.Id == templateId), _folder, "My Game", TestContext.Current.CancellationToken);

        Assert.True(EditorProject.IsProject(folder));
        foreach (var name in ProjectScaffold.StandardFolders)
            Assert.True(Directory.Exists(Path.Combine(folder, "assets", name)), name);
        var settings = GameSettings.Load(Path.Combine(folder, "assets"));
        Assert.Equal("My Game", settings.Title);
        Assert.Equal(DocumentScene.SceneName, settings.StartScene.Name);
        Assert.True(File.Exists(Path.Combine(folder, "assets", "config", "input.json")));

        await using var session = GameSession.Create(new DesktopGameOptions { AssetRoot = Path.Combine(folder, "assets"), Renderer = RendererPreference.Skia, Audio = false },
            NullLoggerFactory.Instance, WindowGraphics.None);
        var game = session.Game;
        game.Start();
        for (var i = 0; i < 600 && (game.Scenes.Current is null || game.Scenes.IsLoading); i++)
        {
            game.Tick(1.0 / 60);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }

        var scene = Assert.IsType<DocumentScene>(game.Scenes.Current);
        Assert.Equal(entities, scene.Document!.Entities.Count);
        var world = scene.World;
        Assert.True(Count<Camera>(world) > 0);
        foreach (var archetype in world.Query<Sprite>())
        {
            foreach (var sprite in archetype.GetSpan<Sprite>())
                Assert.False(sprite.Texture.IsNone);
        }

        Assert.Equal(hasMap, Count<TileMapComponent>(world) > 0);
    }

    private static int Count<T>(Ecs.World world)
    {
        var count = 0;
        foreach (var archetype in world.Query<T>())
            count += archetype.Count;
        return count;
    }

    [Fact]
    public async Task CreatingIntoANonEmptyFolderFails()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Taken"));
        await File.WriteAllTextAsync(Path.Combine(_folder, "Taken", "file.txt"), "x", TestContext.Current.CancellationToken);

        Assert.NotNull(ProjectCreator.Validate(_folder, "Taken"));
        await Assert.ThrowsAsync<IOException>(() => _creator.CreateAsync(_creator.Templates[0], _folder, "Taken", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("My Game", "My Game")]
    [InlineData("  a/b:c ", "a-b-c")]
    [InlineData("///", "New Project")]
    public void FolderNamesAreSafe(string name, string folder) => Assert.Equal(folder, ProjectCreator.ToFolderName(name));

    [Fact]
    public async Task ProjectsResolveFromTheirAssetFolderOrSettingsFile()
    {
        var folder = await _creator.CreateAsync(_creator.Templates[0], _folder, "Resolve Me", TestContext.Current.CancellationToken);

        Assert.True(EditorProject.TryResolve(Path.Combine(folder, "assets", "config", "game.json"), out var resolved));
        Assert.Equal(folder, resolved);
        Assert.False(EditorProject.TryResolve(_folder, out _));
        var project = new EditorProject(folder);
        Assert.Equal("Resolve Me", project.Name);
        Assert.Equal("scenes/main.tscene", project.ToAssetPath(Path.Combine(folder, "assets", "scenes", "main.tscene")));
        Assert.Null(project.ToAssetPath(Path.Combine(folder, "outside.txt")));
    }
}
