using Talesmith.Editor.Assets.Health;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;

namespace Talesmith.Editor.Tests.Assets;

public sealed class AssetHealthTests
{
    [Fact]
    public void AProjectWithNothingToListShowsTheHealthyMessage() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync("empty");
        var health = Create(editor);
        Assert.False(health.IsEmpty);

        await health.LoadAsync();

        Assert.Empty(health.Sections);
        Assert.True(health.IsEmpty);
    });

    [Fact]
    public void UnreferencedAssetsAreListedInsteadOfTheHealthyMessage() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync("empty");
        await editor.Get<IProjectService>().Database!.CreateFileAsync("unused.png", new byte[] { 137, 80, 78, 71 });
        var health = Create(editor);

        await health.LoadAsync();

        Assert.Equal(0, health.ProblemCount);
        var section = Assert.Single(health.Sections);
        Assert.Equal("Unreferenced assets", section.Title);
        Assert.False(health.IsEmpty);
    });

    private static AssetHealthViewModel Create(EditorFixture editor) =>
        new(editor.Get<IProjectService>(), editor.Get<ISelectionService>(), editor.Get<LayoutService>());
}
