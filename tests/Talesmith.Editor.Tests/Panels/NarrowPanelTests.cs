using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Talesmith.Editor.Assets.Browser;
using Talesmith.Editor.Console;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Lighting;
using Talesmith.Editor.Particles;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Tests.Panels;

public sealed class NarrowPanelTests
{
    public static TheoryData<string, double> Panels => new()
    {
        { "console", 250 }, { "console", 600 }, { "assets", 250 }, { "assets", 600 },
        { "lighting", 250 }, { "lighting", 600 }, { "particles", 250 }, { "particles", 600 }
    };

    [Theory]
    [MemberData(nameof(Panels))]
    public void EveryButtonOfAPanelsToolbarStaysInThePanel(string panel, double width) => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var view = await CreateAsync(editor, panel);
        var stage = new Panel { Width = width, Height = 400, Children = { view } };
        var window = new Window { Width = 800, Height = 600, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var controls = view.GetVisualDescendants().OfType<SplitBar>().SelectMany(bar => bar.GetVisualDescendants())
                .OfType<Control>().Where(c => c.IsEffectivelyVisible && c is Button or ToggleButton or ToggleSwitch or SearchBox or NumberField).Distinct().ToList();
            Assert.NotEmpty(controls);
            foreach (var control in controls)
            {
                var bounds = new Rect(control.TranslatePoint(default, stage)!.Value, control.Bounds.Size);
                Assert.True(bounds.X >= 0 && bounds.Right <= width + 0.5, $"{control.GetType().Name} {ToolTip.GetTip(control)} spans {bounds.X} to {bounds.Right} in {width}");
            }

            if (view is AssetBrowserView)
                Assert.Equal(width >= 420, view.FindControl<TreeView>("Folders")!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    private static async Task<Control> CreateAsync(EditorFixture editor, string panel)
    {
        switch (panel)
        {
            case "console":
                return editor.Get<ConsolePanel>().CreateContent();
            case "assets":
                return editor.Get<AssetsPanel>().CreateContent();
            case "lighting":
                editor.Document.CreateEntity("Torch", null,
                [
                    new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }),
                    new ComponentDocument(LightingNames.Light, new JsonObject { ["type"] = "point", ["castsShadows"] = true })
                ]);
                return editor.Get<LightingPanel>().CreateContent();
            default:
                var particles = editor.Get<ParticleEditorViewModel>();
                await editor.WaitAsync(() => particles.Gallery.Tiles.Count > 0);
                editor.Get<ISelectionService>().Clear();
                particles.Apply(particles.Gallery.Tiles.Single(t => t.Name == "Fire"));
                return editor.Get<ParticleEditorPanel>().CreateContent();
        }
    }

    [Fact]
    public void TheTextureInspectorsSegmentsShowTheirWholeContentInANarrowInspector() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var view = new InspectorView(editor.Get<InspectorServices>());
        var stage = new Panel { Width = 250, Height = 700, Children = { view } };
        var window = new Window { Width = 800, Height = 800, Content = stage };
        window.Show();
        try
        {
            editor.Get<ISelectionService>().SelectAssets([editor.Get<IProjectService>().Database!.Assets.First(a => a.Kind == Talesmith.Assets.AssetKind.Texture).Guid]);
            window.UpdateLayout();
            var segments = view.GetVisualDescendants().OfType<SegmentedControlItem>().Where(s => s.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(segments);
            foreach (var segment in segments)
            {
                var shown = segment.Bounds.Width;
                segment.Measure(Size.Infinity);
                Assert.True(shown >= segment.DesiredSize.Width - 0.5, $"{segment.Content} is {shown} wide, less than the {segment.DesiredSize.Width} it needs");
            }
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(900, 200, 200)]
    [InlineData(250, 180, 200)]
    public void TheConsolesFilterBoxTakesTheRoomItHas(double width, double least, double most) => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var view = editor.Get<ConsolePanel>().CreateContent();
        var stage = new Panel { Width = width, Height = 400, Children = { view } };
        var window = new Window { Width = 800, Height = 600, Content = stage };
        window.Show();
        try
        {
            window.UpdateLayout();
            var filter = view.GetVisualDescendants().OfType<SearchBox>().Single().Bounds.Width;
            Assert.InRange(filter, least, most);
        }
        finally
        {
            window.Close();
        }
    });
}
