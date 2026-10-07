using Avalonia.Controls;
using Talesmith.Assets;
using Talesmith.Editor.Assets.SpriteEditor;
using Talesmith.Editor.Projects;

namespace Talesmith.Editor.Tests.Assets;

public sealed class SpriteEditorLayoutTests
{
    [Theory]
    [InlineData(1200, 270, 260)]
    [InlineData(742, 215, 207)]
    [InlineData(590, 190, 170)]
    public void TheSideColumnsGiveUpWidthSoTheCanvasKeepsRoom(double width, double slicing, double sprites) => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var texture = editor.Get<IProjectService>().Database!.Assets.First(a => a.Kind == AssetKind.Texture);
        await editor.Get<SpriteEditorService>().OpenAsync(texture);
        var view = (SpriteEditorView)editor.Get<SpriteEditorPanel>().CreateContent();
        var stage = new Panel { Width = width, Height = 500, Children = { view } };
        var window = new Window { Width = 1300, Height = 600, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var columns = view.FindControl<Grid>("Body")!.ColumnDefinitions;

            Assert.Equal(slicing, columns[0].ActualWidth);
            Assert.Equal(sprites, columns[4].ActualWidth);
            Assert.True(columns[2].ActualWidth >= Math.Min(320, width - slicing - sprites - 2), $"The canvas is {columns[2].ActualWidth} wide");
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void AColumnKeepsTheWidthItsSplitterWasDraggedTo() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var texture = editor.Get<IProjectService>().Database!.Assets.First(a => a.Kind == AssetKind.Texture);
        await editor.Get<SpriteEditorService>().OpenAsync(texture);
        var view = (SpriteEditorView)editor.Get<SpriteEditorPanel>().CreateContent();
        var stage = new Panel { Width = 1200, Height = 500, Children = { view } };
        var window = new Window { Width = 1300, Height = 600, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var columns = view.FindControl<Grid>("Body")!.ColumnDefinitions;

            columns[0].Width = new GridLength(320);
            stage.Width = 1100;
            window.UpdateLayout();

            Assert.Equal(320, columns[0].ActualWidth);
            Assert.Equal(260, columns[4].ActualWidth);
        }
        finally
        {
            window.Close();
        }
    });
}
