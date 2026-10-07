using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Gizmos;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Tests.Viewport;

public sealed class GizmoTests
{
    private static readonly Pointer Mouse = new(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

    [Fact]
    public void DraggingTheMoveHandleIsOneUndoStep() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var undo = editor.Get<IUndoService>();
        var context = Prepare(editor);
        var a = model.CreateEntity("A", null, [new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) })]);
        var b = model.CreateEntity("B", null, [new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(40, 0) })]);
        await editor.World.WhenIdle;
        editor.Get<ISelectionService>().SelectEntities([a.Id, b.Id]);
        undo.Seal();
        var steps = undo.UndoSteps.Count;
        var tool = editor.Get<IEnumerable<IViewportTool>>().OfType<MoveTool>().Single();

        var start = context.ToScreen(new Vector2(40, 0));
        tool.PointerPressed(context, Args(context, start, pressed: true));
        for (var i = 1; i <= 5; i++)
            tool.PointerMoved(context, Args(context, start + new global::Avalonia.Vector(i * 10, i * 4), pressed: true));
        tool.PointerReleased(context, Args(context, start + new global::Avalonia.Vector(50, 20), pressed: false));

        var moved = JsonValues.Vector(model.GetProperty(a.Id, "Transform", "position"))!.Value;
        Assert.NotEqual(Vector2.Zero, moved);
        Assert.Equal(moved + new Vector2(40, 0), JsonValues.Vector(model.GetProperty(b.Id, "Transform", "position")));
        Assert.Equal(steps + 1, undo.UndoSteps.Count);

        undo.Undo();
        Assert.Equal(Vector2.Zero, JsonValues.Vector(model.GetProperty(a.Id, "Transform", "position")));
        Assert.Equal(new Vector2(40, 0), JsonValues.Vector(model.GetProperty(b.Id, "Transform", "position")));
    });

    [Fact]
    public void DraggingALightRadiusHandleIsOneUndoStep() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var undo = editor.Get<IUndoService>();
        var context = Prepare(editor);
        var light = editor.Get<HierarchyViewModel>().Create("light.point", null)!.Value;
        model.SetProperty(light, "Transform", "position", new JsonArray(0, 0));
        await editor.World.WhenIdle;
        undo.Seal();
        var steps = undo.UndoSteps.Count;
        var type = model.Get(light).Components.Single(c => c.Type.EndsWith("Light2D", StringComparison.Ordinal)).Type;
        var before = model.GetProperty(light, type, "radius")?.DeepClone();

        var gizmos = editor.Get<GizmoLayer>();
        Render(gizmos, context);
        var handle = gizmos.Handles.Single(h => h.Entity == light && h.Path == "radius");
        var at = context.ToScreen(handle.Position);
        Assert.True(gizmos.PointerPressed(context, Args(context, at, pressed: true)));
        Assert.True(gizmos.PointerMoved(context, Args(context, at + new global::Avalonia.Vector(30, 0), pressed: true)));
        Assert.True(gizmos.PointerMoved(context, Args(context, at + new global::Avalonia.Vector(60, 0), pressed: true)));
        Assert.True(gizmos.PointerReleased(context, Args(context, at + new global::Avalonia.Vector(60, 0), pressed: false)));

        var radius = JsonValues.Number(model.GetProperty(light, type, "radius"))!.Value;
        Assert.True(radius > handle.Position.X, $"radius {radius}");
        Assert.Equal(steps + 1, undo.UndoSteps.Count);
        undo.Undo();
        Assert.True(JsonNode.DeepEquals(before, model.GetProperty(light, type, "radius")));
    });

    [Fact]
    public void CameraGizmoShowsTheDesignSizeInFitMode()
    {
        var settings = new GameSettings { WindowWidth = 1280, WindowHeight = 800, View = new ViewSettings { Width = 640, Height = 360, IntegerScale = true } };

        var (view, atWindowSize) = CameraGizmo.Areas(settings, new Vector2(100, 50), 2);

        Assert.Equal(new Rect2(-60, -40, 320, 180), view);
        Assert.Null(atWindowSize);
    }

    [Fact]
    public void CameraGizmoAddsWhatTheWindowShowsInExpandAndCropModes()
    {
        var expand = new GameSettings { WindowWidth = 1280, WindowHeight = 800, View = new ViewSettings { ScaleMode = ViewScaleMode.Expand } };
        var crop = expand with { WindowWidth = 1920, WindowHeight = 800, View = expand.View with { ScaleMode = ViewScaleMode.Crop } };

        var (expandView, expandWindow) = CameraGizmo.Areas(expand, Vector2.Zero, 1);
        var (cropView, cropWindow) = CameraGizmo.Areas(crop, Vector2.Zero, 1);

        Assert.Equal(new Rect2(-640, -360, 1280, 720), expandView);
        Assert.Equal(new Rect2(-640, -400, 1280, 800), expandWindow);
        Assert.Equal(new Rect2(-640, -360, 1280, 720), cropView);
        Assert.Equal(new Rect2(-640, -800 / 3f, 1280, 1600 / 3f), cropWindow);
        Assert.Null(CameraGizmo.Areas(expand with { WindowHeight = 720 }, Vector2.Zero, 1).AtWindowSize);
    }

    [Fact]
    public void CameraGizmoShowsTheWindowSizeWithoutAScaleMode()
    {
        var settings = new GameSettings { WindowWidth = 1280, WindowHeight = 800 };

        var (view, atWindowSize) = CameraGizmo.Areas(settings, Vector2.Zero, 0.5f);

        Assert.Equal(new Rect2(-1280, -800, 2560, 1600), view);
        Assert.Null(atWindowSize);
    }

    private static ViewportToolContext Prepare(EditorFixture editor)
    {
        var context = editor.Get<ViewportToolContext>();
        context.Attach(new Border());
        var camera = context.Camera;
        camera.SetViewSize(new Size(800, 600));
        camera.Set(Vector2.Zero, 1);
        editor.Get<ViewportOptions>().SnapToGrid = false;
        return context;
    }

    private static void Render(GizmoLayer gizmos, ViewportToolContext context)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 600));
        using var drawing = bitmap.CreateDrawingContext();
        gizmos.Render(context, drawing);
    }

    private static ViewportPointerEventArgs Args(ViewportToolContext context, Point position, bool pressed)
    {
        var kind = pressed ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.LeftButtonReleased;
        var properties = new PointerPointProperties(pressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None, kind);
        var source = new PointerEventArgs(InputElement.PointerMovedEvent, context.View, Mouse, null, position, 0, properties, KeyModifiers.None);
        return new ViewportPointerEventArgs(source, position, context.ToWorld(position), properties, pressed ? 1 : 0);
    }
}
