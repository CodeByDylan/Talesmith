using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;

namespace Talesmith.EndToEnd.Tests;

/// <summary>A project made from each editor template opens its start scene and plays, as after File › New project.</summary>
public sealed class TemplateTests : IDisposable
{
    private readonly Workspace _workspace = new();
    private readonly ProjectCreator _creator = new([new EmptyTemplate(), new HexAdventureTemplate(), new PlatformerTemplate()]);

    public void Dispose() => _workspace.Dispose();

    [Theory]
    [InlineData("empty")]
    [InlineData("hex-adventure")]
    [InlineData("platformer")]
    public async Task ANewProjectRunsItsStartSceneForTwoSecondsWithoutProblems(string template)
    {
        var folder = await _creator.CreateAsync(_creator.Templates.Single(t => t.Id == template), _workspace.Folder, $"New {template}",
            TestContext.Current.CancellationToken);
        Assert.True(EditorProject.IsProject(folder));
        using var loggers = new RecordingLoggers();

        await using var game = await HeadlessGame.StartAsync(Path.Combine(folder, "assets"), loggers.Factory);
        var start = game.Game.FrameCount;
        game.Run(120, render: true);
        var image = game.Capture();

        Assert.IsType<DocumentScene>(game.Game.Scenes.Current);
        Assert.Equal(start + 121, game.Game.FrameCount);
        Assert.True(game.Count<Camera>() > 0);
        Assert.True(game.Count<Sprite>() > 0);
        Assert.True(DistinctColors(image) > 8, "The frame should show the scene, not a flat color.");
        Assert.Empty(loggers.Problems);
    }

    private static int DistinctColors(Imaging.ImageData image)
    {
        var colors = new HashSet<int>();
        for (var y = 0; y < image.Height; y += 16)
        {
            for (var x = 0; x < image.Width; x += 16)
                colors.Add(image.GetPixel(x, y).GetHashCode());
        }

        return colors.Count;
    }
}
