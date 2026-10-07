using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Assets.Inspectors;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Tests.Inspector;

public sealed class InspectorViewTests
{
    private const string Mover = """
        namespace Fixture;

        public sealed class Mover : Script
        {
            public float Speed = 4;
        }
        """;

    [Fact]
    public void SelectedAssetsShowTheAssetInspectorInTheInspector() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var view = new InspectorView(editor.Get<InspectorServices>());
        var asset = editor.Get<IProjectService>().Database!.Assets.First(a => a.Kind == Talesmith.Assets.AssetKind.Texture);

        editor.Get<ISelectionService>().SelectAssets([asset.Guid]);
        Assert.IsType<AssetInspectorView>(Body(view));

        editor.Get<ISelectionService>().SelectEntities([editor.Document.Entities[0].Id]);
        Assert.IsNotType<AssetInspectorView>(Body(view));
    });

    [Fact]
    public void ScriptFieldsFollowRecompilation() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => WriteScript(folder, Mover));
        var scripts = editor.Get<IScriptService>();
        _ = editor.Get<EditSessionScripts>();
        await WaitIdleAsync(editor, scripts);
        var entity = editor.Document.Entities[0].Id;
        var type = ComponentRegistry.GetTypeName(typeof(ScriptComponent));
        editor.Document.AddComponent(entity, new ComponentDocument(type, new JsonObject
        {
            ["scripts"] = new JsonArray(new JsonObject { ["type"] = "Fixture.Mover", ["enabled"] = true, ["fields"] = new JsonObject() })
        }));
        var view = new InspectorView(editor.Get<InspectorServices>());
        editor.Get<ISelectionService>().SelectEntities([entity]);
        Assert.True(Labels(view).Contains("Speed"), string.Join("|", Labels(view)));
        Assert.DoesNotContain("Jump Height", Labels(view));

        WriteScript(editor.Get<IProjectService>().Project.Folder, Mover.Replace("public float Speed = 4;", "public float Speed = 4;\n    public float JumpHeight = 2;",
            StringComparison.Ordinal));
        Assert.True((await scripts.CompileAsync()).Success);
        await WaitIdleAsync(editor, scripts);
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("Jump Height", Labels(view));
    });

    [Fact]
    public void SpriteRegionFollowsItsSliceUntilSetAndFollowsAgainAfterReset() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var entity = editor.Document.Entities.First(e => e.FindComponent("Sprite")?.Data["sprite"] is not null && e.FindComponent("Sprite")!.Data["source"] is null);
        var view = new InspectorView(editor.Get<InspectorServices>());
        editor.Get<ISelectionService>().SelectEntities([entity.Id]);
        var source = view.Data!.Values.Single(v => v is { Component: "Sprite", Path: "source" });

        Assert.True(source.IsAuto);
        var slice = JsonValues.Rect(source.Get());
        Assert.NotNull(slice);
        Assert.True(slice!.Value.Width > 0);
        Assert.NotNull(Shown(view).OfType<PropertyRow>().Single(r => r.Label == "Source").Accessory);

        source.Update(node => node);
        Assert.False(source.IsAuto);
        Assert.Equal(slice, JsonValues.Rect(editor.Document.Get(entity.Id).FindComponent("Sprite")!.Data["source"]));

        source.ResetToAuto();
        Assert.True(source.IsAuto);
        Assert.False(editor.Document.Get(entity.Id).FindComponent("Sprite")!.Data.ContainsKey("source"));
    });

    [Fact]
    public void SwitchingBetweenEntitiesWithTheSameComponentsReusesTheRows() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var first = model.CreateEntity("First");
        var second = model.CreateEntity("Second");
        model.SetProperty(second.Id, "Transform", "position", new JsonArray(40, 50));
        var view = new InspectorView(editor.Get<InspectorServices>());
        var selection = editor.Get<ISelectionService>();

        selection.SelectEntities([first.Id]);
        var row = Shown(view).OfType<PropertyRow>().First(r => r.Label == "Position");
        var position = view.Data!.Values.Single(v => v is { Component: "Transform", Path: "position" });
        selection.SelectEntities([second.Id]);

        Assert.Same(row, Shown(view).OfType<PropertyRow>().First(r => r.Label == "Position"));
        Assert.Equal(new System.Numerics.Vector2(40, 50), JsonValues.Vector(position.Get()));
        position.Set(new JsonArray(7, 8));
        Assert.Equal(new System.Numerics.Vector2(7, 8), JsonValues.Vector(model.GetProperty(second.Id, "Transform", "position")));
        Assert.NotEqual(new System.Numerics.Vector2(7, 8), JsonValues.Vector(model.GetProperty(first.Id, "Transform", "position")));

        selection.SelectEntities([model.Entities.First(e => e.FindComponent("Camera") is not null).Id]);
        Assert.NotSame(row, Shown(view).OfType<PropertyRow>().First(r => r.Label == "Position"));
        selection.SelectEntities([first.Id]);
        Assert.Same(row, Shown(view).OfType<PropertyRow>().First(r => r.Label == "Position"));
        Assert.Equal(JsonValues.Vector(model.GetProperty(first.Id, "Transform", "position")), JsonValues.Vector(view.Data!.Values.Single(v => v is { Component: "Transform", Path: "position" }).Get()));
    });

    [Fact]
    public void SwitchingBetweenEntitiesWithOtherComponentsKeepsTheirControlsInTheTree() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var entity = model.CreateEntity("Entity");
        var camera = model.Entities.First(e => e.FindComponent("Camera") is not null);
        var view = new InspectorView(editor.Get<InspectorServices>());
        var window = new Window { Content = view };
        window.Show();
        try
        {
            var selection = editor.Get<ISelectionService>();

            selection.SelectEntities([entity.Id]);
            var page = view.Shown;
            var row = Shown(view).OfType<PropertyRow>().First(r => r.Label == "Position");
            var detached = 0;
            row.DetachedFromLogicalTree += (_, _) => detached++;
            selection.SelectEntities([camera.Id]);
            Assert.False(page!.IsVisible);
            selection.SelectEntities([entity.Id]);

            Assert.Same(page, view.Shown);
            Assert.True(page.IsVisible);
            Assert.Same(row, Shown(view).OfType<PropertyRow>().First(r => r.Label == "Position"));
            Assert.Equal(0, detached);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void CollapsedSectionsStayCollapsedInTheirInspectorOnly() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var entity = model.CreateEntity("Entity");
        var camera = model.Entities.First(e => e.FindComponent("Camera") is not null);
        var selection = editor.Get<ISelectionService>();
        var view = new InspectorView(editor.Get<InspectorServices>());

        selection.SelectEntities([entity.Id]);
        Section(view, "Transform").IsExpanded = false;
        selection.SelectEntities([camera.Id]);
        Assert.False(Section(view, "Transform").IsExpanded);

        var other = new InspectorView(editor.Get<InspectorServices>());
        selection.SelectEntities([entity.Id]);
        Assert.True(Section(other, "Transform").IsExpanded);
    });

    [Fact]
    public void TheFirstSelectionOfAKindShowsAPageBuiltWhileIdle() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        await editor.Get<IScriptService>().WhenIdle;
        var hero = editor.Document.Entities.Single(e => e.Name == "Hero");
        var view = new InspectorView(editor.Get<InspectorServices>());
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var built = view.GetLogicalDescendants().OfType<ScrollViewer>().Where(s => !s.IsVisible).ToList();

            editor.Get<ISelectionService>().SelectEntities([hero.Id]);

            Assert.Contains(view.Shown, built);
            Assert.Contains("Hero", Shown(view).OfType<TextBox>().Select(t => t.Text));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TheInspectorKeepsItsPageWhilePlayModeStarts() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        editor.Get<ISelectionService>().Clear();
        var view = new InspectorView(editor.Get<InspectorServices>());
        var settings = view.Shown;
        var play = editor.Get<IPlayModeService>();
        Control? whileStarting = null;
        play.StateChanged += (_, _) =>
        {
            if (play.State == PlayState.Starting)
                whileStarting = view.Shown;
        };

        await play.PlayAsync();
        Assert.Same(settings, whileStarting);
        Assert.NotSame(settings, view.Shown);

        await play.StopAsync();
        Assert.True(view.IsShowingScene);
    });

    [Fact]
    public void SceneBackgroundShowsTheGamesColorUntilOverridden() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var view = new InspectorView(editor.Get<InspectorServices>());
        editor.Get<ISelectionService>().Clear();
        var background = view.Data!.Values.Single(v => v.Path == "clearColor");
        background.ResetToAuto();

        Assert.True(background.IsAuto);
        Assert.Equal(editor.Get<IProjectService>().Settings.ClearColor, JsonValues.Color(background.Get()));

        background.Set(JsonValues.WriteColor(new Talesmith.Mathematics.Color(10, 20, 30)));
        Assert.False(background.IsAuto);
        Assert.Equal(new Talesmith.Mathematics.Color(10, 20, 30), editor.Document.Document.Environment.ClearColor);

        background.ResetToAuto();
        Assert.True(background.IsAuto);
        Assert.Null(editor.Document.Document.Environment.ClearColor);
    });

    [Fact]
    public void CollisionMasksUseTheLayerMaskEditor() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var definition = editor.Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ComponentRegistry>().Find(typeof(Talesmith.Physics.Collider2D))!;
        var mask = definition.Properties.Single(p => p.Name == "collisionMask");
        var layer = definition.Properties.Single(p => p.Name == "layer");

        Assert.True(mask.IsLayerMask);
        Assert.Equal(Talesmith.Authoring.LayerSet.Physics, mask.Layers);
        Assert.False(layer.IsLayerMask);
        Assert.Equal(Talesmith.Authoring.LayerSet.Physics, layer.Layers);

        var entity = editor.Document.Entities[0].Id;
        editor.Document.AddComponent(entity, new ComponentDocument(definition.TypeName, definition.CreateDefault()));
        var view = new InspectorView(editor.Get<InspectorServices>());
        editor.Get<ISelectionService>().SelectEntities([entity]);
        var row = Shown(view).OfType<PropertyRow>().Single(r => r.Label == mask.Label);
        Assert.Contains(row.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Everything");
    });

    [Theory]
    [InlineData(uint.MaxValue, "Everything")]
    [InlineData(0u, "Nothing")]
    [InlineData(1u, "Default")]
    [InlineData(0b110u, "Layer 1, Layer 2")]
    [InlineData(0b1110u, "3 layers")]
    [InlineData(~2u, "Everything but Layer 1")]
    public void LayerMasksReadAsTheirLayers(uint mask, string expected)
    {
        var names = Enumerable.Range(0, 32).Select(i => i == 0 ? "Default" : $"Layer {i}").ToList();
        Assert.Equal(expected, LayerEditorProvider.Summary(mask, names));
    }

    [Fact]
    public void LayerMasksSaveAsTheirFieldsType()
    {
        Assert.Equal(-1, LayerEditorProvider.ToJson(uint.MaxValue, typeof(int)).GetValue<int>());
        Assert.Equal(uint.MaxValue, LayerEditorProvider.ToJson(uint.MaxValue, typeof(uint)).GetValue<uint>());
        Assert.Equal(uint.MaxValue, LayerEditorProvider.ToMask(JsonValue.Create(-1)));
        Assert.Equal(uint.MaxValue, LayerEditorProvider.ToMask(JsonValue.Create(4294967295L)));
    }

    private static Control? Body(InspectorView view) => view.Shown;

    private static IEnumerable<global::Avalonia.LogicalTree.ILogical> Shown(InspectorView view) => view.Shown?.GetSelfAndLogicalDescendants() ?? [];

    private static PropertyGroup Section(InspectorView view, string header) => Shown(view).OfType<PropertyGroup>().Single(g => g.Header?.ToString() == header);

    private static List<string> Labels(InspectorView view) =>
    [
        .. Shown(view).OfType<PropertyGroup>().Select(g => g.Header?.ToString() ?? ""),
        .. Shown(view).OfType<PropertyRow>().Select(r => r.Label ?? "")
    ];

    private static void WriteScript(string projectFolder, string source)
    {
        var path = Path.Combine(projectFolder, "assets", "scripts", "Mover.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }

    private static async Task WaitIdleAsync(EditorFixture editor, IScriptService scripts)
    {
        await editor.WaitAsync(() => scripts.WhenIdle.IsCompleted && scripts.LastResult is not null, 60000);
        await scripts.WhenIdle;
    }
}
