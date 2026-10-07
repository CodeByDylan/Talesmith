using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Lighting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Particles;
using Talesmith.Editor.Selection;
using Talesmith.Runtime.Serialization;
using Talesmith.Screenshots.Capture;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The particle editor on a campfire made from the Fire preset, with its color and size curves open and the preview playing.</summary>
internal sealed class ParticleEditorScene : EditorWindowScene
{
    public override string Name => "editor-particles";

    protected override void Customize(Window window)
    {
        var particles = Editor.Get<ParticleEditorViewModel>();
        Editor.Get<ISelectionService>().Clear();
        RenderLoop.Settle(100);
        particles.Apply(particles.Gallery.Tiles.First(t => t.Name == "Fire"));
        Editor.Get<ISceneDocumentService>().Active!.Rename(Editor.Get<ISelectionService>().Entities[0], "Campfire");
        foreach (var card in particles.Cards)
            card.IsExpanded = card.Info.Key is "emission" or "colorOverLifetime" or "sizeOverLifetime" or "renderer";
        Maximize(PanelIds.Particles);
        RenderLoop.Settle(400);
        window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Modules").ScrollToHome();
        RenderLoop.Settle(3000);
    }

    private void Maximize(string panel)
    {
        var layout = Editor.Get<LayoutService>();
        layout.ShowPanel(panel);
        if (layout.Layout.FindPanel(panel) is { } group && !ReferenceEquals(layout.Layout.MaximizedGroup, group))
            layout.Layout.ToggleMaximize(group.Id);
    }
}

/// <summary>The particle editor with nothing selected: the gallery of built-in presets, each playing in its own small preview.</summary>
internal sealed class ParticlePresetsScene : EditorWindowScene
{
    public override string Name => "editor-particle-presets";

    protected override void Customize(Window window)
    {
        Editor.Get<ISelectionService>().Clear();
        var layout = Editor.Get<LayoutService>();
        layout.ShowPanel(PanelIds.Particles);
        if (layout.Layout.FindPanel(PanelIds.Particles) is { } group && !ReferenceEquals(layout.Layout.MaximizedGroup, group))
            layout.Layout.ToggleMaximize(group.Id);
        RenderLoop.Settle(1200);
    }
}

/// <summary>The Lighting panel on a night scene with torches, a lamp, the moon, shadow casters and a glowing window.</summary>
internal sealed class LightingPanelScene : EditorWindowScene
{
    public override string Name => "editor-lighting";

    protected override void Customize(Window window)
    {
        var model = Editor.Get<ISceneDocumentService>().Active!;
        Light(model, "Moonlight", "directional", "#9DB4FF", 0.45, 0, castsShadows: true, layers: 1u);
        var torch = Light(model, "Torch (gate)", "point", "#FFB45C", 1.6, 220, castsShadows: true, layers: 3u);
        Light(model, "Torch (well)", "point", "#FF9A3C", 1.3, 180, castsShadows: true, layers: 1u);
        Light(model, "Street lamp", "spot", "#FFE6B0", 1.1, 320, castsShadows: false, layers: uint.MaxValue);
        Light(model, "Magic crystal", "point", "#7C5CFF", 0.9, 140, castsShadows: false, layers: uint.MaxValue, enabled: false);
        Caster(model, "Gate wall", "box", 0);
        Caster(model, "Well", "circle", 1);
        Caster(model, "Barrel", "circle", 1);
        model.CreateEntity("Tavern window", null,
        [
            new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(40, -60) }),
            new ComponentDocument(LightingNames.Emissive, new JsonObject { ["color"] = "#FFD27A", ["intensity"] = 1.2 })
        ]);
        var environment = LightingPresetFor("Night").ApplyTo(model.Document.Environment);
        model.SetEnvironment(environment, "Apply Night lighting");
        Editor.Get<ISelectionService>().SelectEntity(torch);
        var layout = Editor.Get<LayoutService>();
        layout.ShowPanel(PanelIds.Lighting);
        if (layout.Layout.FindPanel(PanelIds.Lighting) is { } group && !ReferenceEquals(layout.Layout.MaximizedGroup, group))
            layout.Layout.ToggleMaximize(group.Id);
        RenderLoop.Settle(800);
    }

    private static LightingPreset LightingPresetFor(string name) => LightingPreset.BuiltIn.First(p => p.Name == name);

    private static Guid Light(SceneDocumentModel model, string name, string type, string color, double intensity, double radius, bool castsShadows, uint layers,
        bool enabled = true) =>
        model.CreateEntity(name, null,
        [
            new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(-280 + 140 * model.Document.Entities.Count % 560, 60 * (model.Document.Entities.Count % 3) - 60) }),
            new ComponentDocument(LightingNames.Light, new JsonObject
            {
                ["enabled"] = enabled,
                ["type"] = type,
                ["color"] = color,
                ["intensity"] = intensity,
                ["radius"] = radius,
                ["castsShadows"] = castsShadows,
                ["shadowLayers"] = layers
            })
        ]).Id;

    private static void Caster(SceneDocumentModel model, string name, string shape, int layer) =>
        model.CreateEntity(name, null,
        [
            new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 40) }),
            new ComponentDocument(LightingNames.ShadowCaster, new JsonObject { ["shape"] = shape, ["layer"] = layer })
        ]);
}
