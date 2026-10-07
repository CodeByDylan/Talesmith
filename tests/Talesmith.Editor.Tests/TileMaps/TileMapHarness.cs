using Avalonia.Input;
using Talesmith.Assets.Maps;
using Talesmith.Editor.Selection;
using Talesmith.Editor.TileMaps;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;

namespace Talesmith.Editor.Tests.TileMaps;

/// <summary>An editor with a template project's map selected for editing, and simulated pointer and key input for the tile tools.</summary>
internal sealed class TileMapHarness : IAsyncDisposable
{
    private readonly Pointer _pointer = new(Pointer.GetNextFreeId(), PointerType.Mouse, true);

    private TileMapHarness(EditorFixture fixture) => Fixture = fixture;

    public EditorFixture Fixture { get; }

    public TileMapEditor Editor => Fixture.Get<TileMapEditor>();

    public TileMap Map => Editor.Map!;

    public TileLayer Layer => Editor.ActiveTileLayer!;

    public IUndoService Undo => Fixture.Get<IUndoService>();

    public ViewportToolContext Context => Fixture.Get<ViewportToolContext>();

    public ToolManager Tools => Fixture.Get<ToolManager>();

    /// <summary>Opens the square platformer template ("platformer") or the hex adventure template ("hex-adventure") with its map edited.</summary>
    public static async Task<TileMapHarness> OpenAsync(string template)
    {
        var fixture = await EditorFixture.OpenAsync(template);
        var harness = new TileMapHarness(fixture);
        var entity = fixture.Document.Entities.First(e => e.FindComponent("TileMapRenderer") is not null);
        fixture.Get<ISelectionService>().SelectEntity(entity.Id);
        Assert.NotNull(harness.Editor.Map);
        harness.Editor.ActiveLayer = harness.Map.TileLayers[0];
        return harness;
    }

    public T Tool<T>()
        where T : IViewportTool => Tools.Tools.OfType<T>().Single();

    public TileCell Tile(int id) => new(Map.Tilesets[0].Id, id);

    /// <summary>Uses a tool through the viewport's input: activates it, presses on the first cell, moves through the others and releases on the last.</summary>
    public void Drag(IViewportTool tool, IReadOnlyList<GridCoord> cells, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None)
    {
        Tools.ActiveTool = tool;
        Press(cells[0], button, modifiers);
        foreach (var cell in cells.Skip(1))
            Move(cell, button, modifiers);
        Release(cells[^1], button, modifiers);
    }

    public void Click(IViewportTool tool, GridCoord cell, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        Drag(tool, [cell], button, modifiers);

    public void Press(GridCoord cell, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None, int clickCount = 1) =>
        Tools.ActiveTool.PointerPressed(Context, Args(cell, Properties(button, pressed: true), modifiers, clickCount));

    public void Move(GridCoord cell, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        Tools.ActiveTool.PointerMoved(Context, Args(cell, Properties(button, pressed: true), modifiers, 0));

    public void Hover(GridCoord cell) => Tools.ActiveTool.PointerMoved(Context, Args(cell, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, 0));

    public void Release(GridCoord cell, MouseButton button = MouseButton.Left, KeyModifiers modifiers = KeyModifiers.None) =>
        Tools.ActiveTool.PointerReleased(Context, Args(cell, Properties(button, pressed: false), modifiers, 0));

    public bool Key(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        Tools.ActiveTool.KeyDown(Context, new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });

    /// <summary>The tiles of the active layer at cells.</summary>
    public TileCell[] TilesAt(IEnumerable<GridCoord> cells) => [.. cells.Select(Layer.GetCell)];

    public async Task WaitAsync(Func<bool> condition) => await Fixture.WaitAsync(condition);

    public ValueTask DisposeAsync() => Fixture.DisposeAsync();

    private static PointerPointProperties Properties(MouseButton button, bool pressed)
    {
        var raw = !pressed ? RawInputModifiers.None : button == MouseButton.Right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton;
        var kind = (button, pressed) switch
        {
            (MouseButton.Right, true) => PointerUpdateKind.RightButtonPressed,
            (MouseButton.Right, false) => PointerUpdateKind.RightButtonReleased,
            (_, true) => PointerUpdateKind.LeftButtonPressed,
            _ => PointerUpdateKind.LeftButtonReleased
        };
        return new PointerPointProperties(raw, kind);
    }

    private ViewportPointerEventArgs Args(GridCoord cell, PointerPointProperties properties, KeyModifiers modifiers, int clickCount)
    {
        var world = Editor.CellCenter(cell);
        var screen = Context.ToScreen(world);
        var source = new PointerEventArgs(InputElement.PointerMovedEvent, null, _pointer, null, screen, 0, properties, modifiers);
        return new ViewportPointerEventArgs(source, screen, world, properties, clickCount);
    }
}
