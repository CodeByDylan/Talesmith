using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Authoring;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Lighting;
using Talesmith.Physics;
using Talesmith.Runtime.Serialization;
using Talesmith.Screenshots.Capture;
using Talesmith.Scripting;
using Talesmith.UI.Controls;
using Talesmith.VFX;

namespace Talesmith.Screenshots.Scenes;

/// <summary>A script the authoring scenes attach, so the inspector shows a script section.</summary>
public sealed class PatrolScript : Script
{
    [Tooltip("World units per second.")]
    [Range(0, 400)]
    public float Speed = 120;

    [Tooltip("How long to wait at each end.")]
    public float Pause = 0.5f;

    public bool PingPong = true;

    public Vector2 Offset = new(96, 0);
}

/// <summary>The platformer with a richer scene: a hero with a collider, a light, particles and scripts, and nested groups of lights and enemies.</summary>
internal abstract class AuthoringScene : EditorWindowScene
{
    protected Guid Hero { get; private set; }

    protected Guid Lamp { get; private set; }

    protected T Get<T>()
        where T : notnull => Editor.Get<T>();

    protected sealed override void Customize(Window window)
    {
        var project = Get<IProjectService>();
        RenderLoop.Wait(() => project.ScanProgress is null, 30000);
        RenderLoop.Settle(300);
        Populate();
        RenderLoop.Settle(300);
        foreach (var group in window.GetVisualDescendants().OfType<PropertyGroup>())
            group.IsExpanded = true;
        Show(window);
        RenderLoop.Settle(400);
    }

    protected abstract void Show(Window window);

    private void Populate()
    {
        var model = Get<ISceneDocumentService>().Active!;
        var hierarchy = Get<HierarchyViewModel>();
        var templates = Get<EntityTemplates>();
        var registry = Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ComponentRegistry>();
        var scripts = Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ScriptTypeRegistry>();
        var patrol = scripts.GetOrAdd(typeof(PatrolScript));

        Hero = model.Entities.Single(e => e.Name == "Hero").Id;
        var collider = registry.Find(typeof(Collider2D))!;
        var colliderData = collider.CreateDefault();
        colliderData["size"] = new JsonArray(20, 28);
        colliderData["offset"] = new JsonArray(0, -14);
        model.AddComponent(Hero, new ComponentDocument(collider.TypeName, colliderData));
        foreach (var component in templates.Find("light.point")!.Components())
        {
            component.Data["radius"] = 120;
            component.Data["color"] = "#FFD08A";
            model.AddComponent(Hero, component);
        }

        foreach (var component in templates.Find("particles.magic-sparkle")!.Components())
            model.AddComponent(Hero, component);
        var scriptType = registry.Find(typeof(ScriptComponent))!.TypeName;
        model.AddComponent(Hero, new ComponentDocument(scriptType, new JsonObject
        {
            ["scripts"] = new JsonArray(
                new JsonObject { ["type"] = patrol.TypeName, ["enabled"] = true, ["fields"] = new JsonObject { ["speed"] = 160 } },
                new JsonObject { ["type"] = "Game.OldJump", ["enabled"] = true, ["fields"] = new JsonObject { ["height"] = 3 } })
        }));

        var lights = hierarchy.Create(null, null, name: "Lights")!.Value;
        model.SetProperty(lights, "Transform", "position", new JsonArray(0, 0));
        Lamp = hierarchy.Create("light.point", lights, name: "Lamp")!.Value;
        model.SetProperty(Lamp, "Transform", "position", new JsonArray(250, 150));
        model.SetProperty(Lamp, model.Get(Lamp).Components[1].Type, "radius", 90);
        hierarchy.Create("light.spot", lights, name: "Torch");
        var enemies = hierarchy.Create(null, null, name: "Enemies")!.Value;
        var crab = hierarchy.Create("sprite", enemies, name: "Crab")!.Value;
        hierarchy.Create(null, crab, name: "Claw");
        hierarchy.Create("sprite", enemies, name: "Crab");
        hierarchy.Create("audio", null, name: "Waves");
        hierarchy.Tree.SetExpandedRecursive(lights, true);
        hierarchy.Tree.SetExpandedRecursive(enemies, true);
        Get<ISelectionService>().SelectEntity(Hero);
    }

    protected void Frame(Vector2 center, float zoom)
    {
        var camera = Get<ViewportCamera>();
        camera.Set(center, zoom);
    }
}

/// <summary>The inspector of an entity with a sprite, collider, light, particle emitter and scripts.</summary>
internal sealed class EditorInspectorScene : AuthoringScene
{
    public override string Name => "editor-inspector";

    public override Size Size => new(1440, 1560);

    protected override void Show(Window window)
    {
        Frame(new Vector2(128, 140), 2.2f);
        Get<ToolManager>().Select("move");
        RenderLoop.Settle(200);
        foreach (var group in window.GetVisualDescendants().OfType<PropertyGroup>())
        {
            if (group.Header is "Transform" or "Sprite Animator" or "Light 2D")
                group.IsExpanded = false;
        }
    }
}

/// <summary>The hierarchy with nested groups while an entity is dragged into another, showing the drop indicator.</summary>
internal sealed class EditorHierarchyScene : AuthoringScene
{
    public override string Name => "editor-hierarchy";

    protected override void Show(Window window)
    {
        var hierarchy = Get<HierarchyViewModel>();
        var rows = window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("hrow")).ToList();
        if (rows.FirstOrDefault(r => r.DataContext is HierarchyRow { Name: "Torch" }) is { } target)
            DropIndicator.Show(target, DropPosition.Before, ((HierarchyRow)target.DataContext!).Depth * HierarchyRow.IndentStep + 22);
        hierarchy.Filter = "";
    }
}

/// <summary>The searchable Add Component popup with recent and categorized components and scripts.</summary>
internal sealed class EditorAddComponentScene : AuthoringScene
{
    public override string Name => "editor-add-component";

    protected override void Show(Window window)
    {
        Frame(new Vector2(128, 140), 2.2f);
        var button = window.GetVisualDescendants().OfType<Button>().LastOrDefault(b => b.IsEffectivelyVisible && ToolTip.GetTip(b) is "Add a component or script to the selected entities");
        if (button?.Flyout is not Flyout flyout)
            return;
        button.BringIntoView();
        RenderLoop.Settle(200);
        var registry = Get<IProjectService>().EditSession!.Game.Services.GetRequiredService<ComponentRegistry>();
        Get<ProjectState>().Set("inspector.recentComponents", new List<string> { registry.Find(typeof(Rigidbody2D))!.TypeName, registry.Find(typeof(ShadowCaster2D))!.TypeName });
        flyout.ShowAt(button);
    }
}

/// <summary>Move handles on a sprite and a light selected together, with their component gizmos.</summary>
internal sealed class EditorGizmoScene : AuthoringScene
{
    public override string Name => "editor-gizmos";

    protected override void Show(Window window)
    {
        Get<ISelectionService>().SelectEntities([Hero, Lamp]);
        Get<ToolManager>().Select("move");
        Frame(new Vector2(190, 140), 2f);
    }
}

/// <summary>A prefab opened for editing: the hierarchy's breadcrumb leads back to the scene.</summary>
internal sealed class EditorPrefabScene : AuthoringScene
{
    public override string Name => "editor-prefab";

    protected override void Show(Window window)
    {
        var prefabs = Get<PrefabWorkflow>();
        var create = prefabs.CreatePrefabAsync(Hero);
        RenderLoop.Wait(() => create.IsCompleted, 30000);
        if (create.Result is not { } prefab)
            return;
        Get<Talesmith.Editor.Undo.IUndoService>().MarkSaved(Get<ISceneDocumentService>().Active!);
        var open = prefabs.OpenAsync(prefab);
        RenderLoop.Wait(() => open.IsCompleted, 30000);
        var world = Get<IEditWorld>();
        RenderLoop.Wait(() => !world.IsBusy && world.EntityCount > 0, 30000);
        if (Get<ISceneDocumentService>().Active?.Entities is { Count: > 0 } entities)
            Get<ISelectionService>().SelectEntity(entities[0].Id);
        Frame(new Vector2(0, -20), 3f);
    }
}

/// <summary>The inspector in play mode, showing the live values of the hero.</summary>
internal sealed class EditorPlayInspectorScene : AuthoringScene
{
    public override string Name => "editor-play-inspector";

    protected override void Show(Window window)
    {
        var play = Get<IPlayModeService>();
        _ = play.PlayAsync();
        PlayWait.UntilSceneLoaded(play);
        RenderLoop.Settle(600);
        var hierarchy = Get<HierarchyViewModel>();
        RenderLoop.Wait(() => hierarchy.Rows.Count > 0, 10000);
        RenderLoop.Settle(600);
    }
}
