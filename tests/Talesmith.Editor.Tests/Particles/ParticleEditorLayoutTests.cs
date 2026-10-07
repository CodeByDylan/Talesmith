using Avalonia;
using Avalonia.Controls;
using Talesmith.Editor.Particles;
using Talesmith.Editor.Selection;

namespace Talesmith.Editor.Tests.Particles;

public sealed class ParticleEditorLayoutTests
{
    [Theory]
    [InlineData(170, true)]
    [InlineData(700, false)]
    public void TheModulesStayWithinReachInAShortPanel(double height, bool scrolls) => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var particles = editor.Get<ParticleEditorViewModel>();
        await editor.WaitAsync(() => particles.Gallery.Tiles.Count > 0);
        editor.Get<ISelectionService>().Clear();
        particles.Apply(particles.Gallery.Tiles.Single(t => t.Name == "Fire"));
        var view = (ParticleEditorView)editor.Get<ParticleEditorPanel>().CreateContent();
        var stage = new Panel { Width = 590, Height = height, Children = { view } };
        var window = new Window { Width = 800, Height = 900, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var body = view.FindControl<ScrollViewer>("BodyScroll")!;
            var modules = view.FindControl<ScrollViewer>("Modules")!;

            Assert.Equal(scrolls, body.Extent.Height > body.Viewport.Height + 1);
            Assert.True(modules.Bounds.Height >= Math.Min(150, body.Viewport.Height) - 1, $"The modules are {modules.Bounds.Height} pixels tall");
            body.Offset = new Vector(0, body.Extent.Height);
            window.UpdateLayout();
            var top = modules.TranslatePoint(default, body)!.Value.Y;
            Assert.True(top >= 0 && top + modules.Bounds.Height <= body.Viewport.Height + 1, $"The modules start at {top} of {body.Viewport.Height}");
        }
        finally
        {
            window.Close();
        }
    });
}
