using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.Screenshots.Capture;
using Talesmith.Screenshots.Scenes;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Perf;

/// <summary>Opens a stress project in a headless editor and measures how long interactions keep the UI thread busy.</summary>
/// <remarks>Each interaction is timed until its posted work ran and the window was laid out again; frames include the edit game's tick
/// and Avalonia's software rendering of the whole 1600×1000 window.</remarks>
internal static class StressBenchmark
{
    public static int Run(int columns, int rows, int entities)
    {
        var parent = Path.Combine(Path.GetTempPath(), "talesmith-stress", Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(parent);
        try
        {
            var clock = Stopwatch.StartNew();
            var folder = Task.Run(() => StressProject.CreateAsync(parent, columns, rows, entities)).GetAwaiter().GetResult();
            Console.WriteLine($"Generated {columns * rows:N0} cells and {entities:N0} entities in {clock.Elapsed.TotalSeconds:0.0} s");
            Measure(folder, entities);
            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(parent, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Measure(string folder, int entities)
    {
        var clock = Stopwatch.StartNew();
        var editor = EditorFixture.Open(folder);
        var window = new Window { Content = editor.Root, WindowDecorations = WindowDecorations.None };
        RenderLoop.Host(window, 1600, 1000, 1);
        editor.Services.GetRequiredService<WindowHost>().Attach(window, editor.Root, editor.Toasts);
        var project = editor.Get<IProjectService>();
        var world = editor.Get<IEditWorld>();
        var documents = editor.Get<ISceneDocumentService>();
        RenderLoop.Wait(() => project.IsReady && documents.Active is not null && world.World is not null && !world.IsBusy && world.EntityCount >= entities,
            600_000);
        Report("Open project until the scene shows", clock.Elapsed.TotalMilliseconds);
        _meter = new Meter(project.EditSession?.Game);

        var selection = editor.Get<ISelectionService>();
        var ids = documents.Active!.Entities.Select(e => e.Id).ToList();
        var random = new Random(3);
        Settle(window);
        if (Wanted("idle"))
        {
            var overlay = window.GetVisualDescendants().OfType<ViewportOverlay>().First();
            var status = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("status"));
            var visuals = window.GetVisualDescendants().ToList();
            Report("Idle frame: nothing changes", Times(Repeat(40), () => { }, window));
            Report("Idle frame: redraw the status text", Times(Repeat(40), status.InvalidateVisual, window));
            Report("Idle frame: redraw the viewport overlay", Times(Repeat(40), overlay.InvalidateVisual, window));
            Report("Idle frame: redraw every control", Times(Repeat(40), () => visuals.ForEach(v => v.InvalidateVisual()), window));
        }

        if (Wanted("select"))
            Report("Select an entity (hierarchy, inspector, overlay)", Times(Repeat(40), () => selection.SelectEntities([ids[random.Next(ids.Count)]]), window));

        var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "Scroller" && s.FindAncestorOfType<HierarchyView>() is not null);
        var hierarchy = editor.Get<HierarchyViewModel>();
        var groups = hierarchy.Rows.Where(r => r.HasChildren).ToList();
        if (Wanted("reveal"))
        {
            groups.ForEach(g => hierarchy.SetExpanded(g, false));
            var hidden = groups.Select(g => documents.Active!.Entities.First(e => e.Parent == g.Id).Id).ToList();
            Settle(window);
            Report("Select an entity in a collapsed group", Times(hidden.Count, i => selection.SelectEntities([hidden[i]]), window));
            groups.ForEach(g => hierarchy.SetExpanded(g, false));
            Settle(window);
        }

        Report("Expand a group of 100 entities", Times(groups.Count, i => hierarchy.SetExpanded(groups[i], true), window));
        Settle(window);
        var offset = 0.0;
        if (Wanted("scroll"))
            Report("Scroll the hierarchy by a page", Times(Repeat(40), () => scroller.Offset = new Vector(0, offset += 600), window));

        var viewport = editor.Get<ViewportService>();
        var camera = viewport.Camera;
        foreach (var zoom in Wanted("pan") ? new[] { 1f, 0.25f, 0.05f } : [])
        {
            camera.Set(camera.Position, zoom);
            Settle(window);
            Report($"Pan one frame at {zoom * 100:0}% zoom", Times(Repeat(60), () => camera.PanBy(new Vector(23, 11)), window));
        }

        if (Wanted("pan"))
            Report("Zoom one wheel step", Times(Repeat(40), () => camera.ZoomAt(new Point(700, 400), 1.1f, animate: false), window));

        if (Wanted("paint"))
            PaintAndUndo(editor, window, documents);
        window.Close();
        var disposed = editor.Services.DisposeAsync().AsTask();
        RenderLoop.Wait(() => disposed.IsCompleted, 60_000);
        disposed.GetAwaiter().GetResult();
    }

    private static void PaintAndUndo(OpenEditor editor, Window window, ISceneDocumentService documents)
    {
        var maps = editor.Get<TileMapEditor>();
        var mapEntity = documents.Active!.Entities.First(e => e.FindComponent("TileMapRenderer") is not null);
        editor.Get<ISelectionService>().SelectEntities([mapEntity.Id]);
        Settle(window);
        if (maps.Map is not { } map)
        {
            Console.WriteLine("The stress map could not be edited.");
            return;
        }

        maps.ActiveLayer = map.TileLayers[0];
        var tools = editor.Get<ToolManager>();
        var context = editor.Get<ViewportToolContext>();
        var undo = editor.Get<IUndoService>();
        var camera = editor.Get<ViewportService>().Camera;
        camera.Set(maps.CellCenter(GridCoord.Zero), 1);
        Settle(window);

        maps.Brush.Pick(new Talesmith.Assets.Maps.TileCell(map.Tilesets[0].Id, 3));
        tools.ActiveTool = tools.Tools.OfType<BrushTool>().Single();
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var cells = Enumerable.Range(0, 120).Select(i => new GridCoord(i % 40 - 20, i / 40 - 1)).ToList();
        tools.ActiveTool.PointerPressed(context, Args(maps, context, pointer, cells[0], pressed: true));
        var strokeTimes = Times(cells.Count - 1, i => tools.ActiveTool.PointerMoved(context, Args(maps, context, pointer, cells[i + 1], pressed: true)), window);
        tools.ActiveTool.PointerReleased(context, Args(maps, context, pointer, cells[^1], pressed: false));
        Report("Paint: one brush step and its frame", strokeTimes);

        tools.ActiveTool = tools.Tools.OfType<RectangleTool>().Single();
        var corner = new GridCoord(-150, -100);
        var opposite = new GridCoord(150, 100);
        var fill = Times(1, () =>
        {
            tools.ActiveTool.PointerPressed(context, Args(maps, context, pointer, corner, pressed: true));
            tools.ActiveTool.PointerMoved(context, Args(maps, context, pointer, opposite, pressed: true));
            tools.ActiveTool.PointerReleased(context, Args(maps, context, pointer, opposite, pressed: false));
        }, window);
        Report($"Fill a rectangle ({undo.UndoSteps[^1].Description})", fill);
        Report("Undo the rectangle", Times(1, undo.Undo, window));
        Report("Redo the rectangle", Times(1, undo.Redo, window));
    }

    private static Talesmith.Editor.Viewport.Tools.ViewportPointerEventArgs Args(TileMapEditor maps, ViewportToolContext context, Pointer pointer, GridCoord cell,
        bool pressed)
    {
        var world = maps.CellCenter(cell);
        var screen = context.ToScreen(world);
        var properties = new PointerPointProperties(pressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None,
            pressed ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.LeftButtonReleased);
        var source = new PointerEventArgs(InputElement.PointerMovedEvent, null, pointer, null, screen, 0, properties, KeyModifiers.None);
        return new Talesmith.Editor.Viewport.Tools.ViewportPointerEventArgs(source, screen, world, properties, 1);
    }

    /// <summary>One measurement: the whole interaction, and the parts of it the editor's game and its renderer took.</summary>
    /// <param name="Render">Drawing the edit game's frame, which runs on Avalonia's render thread in the editor but here on the UI thread.</param>
    /// <param name="Work">The UI thread's time until the interaction ran and the window was laid out, before the frame was recorded and composited.</param>
    private readonly record struct Sample(double Total, double Game, double Render, double Work = 0)
    {
        public double UiThread => Total - Render;
    }

    /// <summary>Times an interaction until the work it posted ran, the window was laid out and a frame was drawn.</summary>
    private static List<Sample> Times(int count, Action action, Window window) => Times(count, _ => action(), window);

    private static List<Sample> Times(int count, Action<int> action, Window window)
    {
        var samples = new List<Sample>(count);
        for (var i = 0; i < count; i++)
        {
            Drain(window);
            var start = _meter.Snapshot();
            var clock = Stopwatch.StartNew();
            action(i);
            Flush(window);
            var work = _meter.Since(start, clock.Elapsed.TotalMilliseconds).UiThread;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            samples.Add(_meter.Since(start, clock.Elapsed.TotalMilliseconds) with { Work = work });
        }

        return samples;
    }

    private static readonly string? Only = Environment.GetEnvironmentVariable("TALESMITH_PERF_ONLY");

    private static Meter _meter = new(null);

    private static bool Wanted(string section) => Only is null || Only.Contains(section, StringComparison.Ordinal);

    private static int Repeat(int count) => Only is null ? count : count * 5;

    /// <summary>Runs pending frames and work, so the next measurement starts idle.</summary>
    private static void Drain(Window window)
    {
        for (var i = 0; i < 2; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Flush(window);
        }
    }

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Settle(Window window)
    {
        Flush(window);
        RenderLoop.Settle(300);
    }

    private static void Report(string name, double milliseconds) => Report(name, [new Sample(milliseconds, 0, 0)]);

    private static void Report(string name, List<Sample> samples)
    {
        static (double Median, double P95, double Max) Stats(List<double> values)
        {
            values.Sort();
            return (values[values.Count / 2], values[Math.Min(values.Count - 1, (int)Math.Ceiling(values.Count * 0.95) - 1)], values[^1]);
        }

        var ui = Stats([.. samples.Select(s => s.UiThread)]);
        var game = Stats([.. samples.Select(s => s.Game)]);
        var render = Stats([.. samples.Select(s => s.Render)]);
        var work = Stats([.. samples.Select(s => s.Work)]);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{name,-46} UI thread median {ui.Median,7:0.0}  p95 {ui.P95,7:0.0}  max {ui.Max,7:0.0} ms | before the frame median {work.Median,6:0.0}  p95 {work.P95,6:0.0} | game tick median {game.Median,6:0.0} | scene render median {render.Median,6:0.0}  max {render.Max,6:0.0} ms ({samples.Count}x)"));
    }

    /// <summary>Adds up the edit game's tick and render times from its profilers.</summary>
    private sealed class Meter
    {
        private double _game;
        private double _render;

        public Meter(Talesmith.Runtime.Hosting.Game? game)
        {
            if (game is null)
                return;
            game.Profilers.Game.FrameCompleted += p => _game += p.LastFrame?.WorkMilliseconds ?? 0;
            game.Profilers.Render.FrameCompleted += p => _render += p.LastFrame?.WorkMilliseconds ?? 0;
        }

        public (double Game, double Render) Snapshot() => (_game, _render);

        public Sample Since((double Game, double Render) start, double total) => new(total, _game - start.Game, _render - start.Render);
    }
}
