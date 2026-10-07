using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;

namespace Talesmith.Editor.TileMaps.Tools;

/// <summary>The options a tile tool shows in the toolbar.</summary>
[Flags]
public enum TileToolOptions
{
    None = 0,
    Tile = 1,
    Size = 2,
    Orientation = 4,
    ShapeFill = 8,
    Fill = 16,
    Terrain = 32,
    Stamp = 64,
    Weights = 128,
    Selection = 256,
    Objects = 512,
    Range = 1024,
    Collision = 2048
}

/// <summary>Base of the tile tools: available while a map is edited, shows the hovered cell, picks with Alt-click, handles the clipboard and
/// orientation keys, draws the selection and the tool's preview, and gives the viewport the map's cells as its grid.</summary>
public abstract class TileTool(TileMapEditor editor, TileOverlay overlay) : IViewportGridTool
{
    public const string ToolGroup = "Tile";

    private Control? _options;

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract Geometry Icon { get; }

    public abstract string? Shortcut { get; }

    public string Group => ToolGroup;

    public abstract int Order { get; }

    public virtual Cursor? Cursor => new(StandardCursorType.Cross);

    /// <summary>The options shown in the toolbar.</summary>
    public abstract TileToolOptions Options { get; }

    public virtual bool IsOperationInProgress => false;

    protected TileMapEditor Editor { get; } = editor;

    protected TileOverlay Overlay { get; } = overlay;

    /// <summary>Whether Alt-click picks the tile under the pointer into the brush.</summary>
    protected virtual bool PicksWithAlt => (Options & TileToolOptions.Tile) != 0;

    /// <summary>Whether Z, Shift+Z, X and Y turn the brush.</summary>
    protected virtual bool TurnsBrush => (Options & TileToolOptions.Orientation) != 0;

    public bool IsAvailable(ViewportToolContext context) => Editor.Map is not null;

    public bool HasGrid => Editor.Map is not null;

    public string GridName => "Tile grid";

    public Geometry GridIcon => Editor.Map?.Layout.Kind is GridKind.Square or null ? Icons.Grid : Icons.Hexagon;

    public void RenderGrid(ViewportToolContext context, DrawingContext drawing, Color color) => Overlay.Grid.Render(drawing, color);

    public virtual void Activate(ViewportToolContext context) => context.Hint = Description;

    public virtual void Deactivate(ViewportToolContext context) => Editor.HoveredCell = null;

    public void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (Editor.Map is null || Editor.IsBusy)
            return;
        var cell = Editor.CellAt(e.World);
        Editor.HoveredCell = cell;
        if (PicksWithAlt && e.Properties.IsLeftButtonPressed && (e.Modifiers & KeyModifiers.Alt) != 0)
        {
            Pick(cell);
            e.Handled = true;
            context.Invalidate();
            return;
        }

        if (e.Properties.IsLeftButtonPressed || e.Properties.IsRightButtonPressed)
            e.Source.Pointer.Capture(context.View);
        OnPressed(context, e, cell);
        e.Handled = true;
        context.Invalidate();
    }

    public void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (Editor.Map is null)
            return;
        var cell = Editor.CellAt(e.World);
        var changed = Editor.HoveredCell != cell;
        Editor.HoveredCell = cell;
        if (Editor.IsBusy)
            return;
        if (OnMoved(context, e, cell, changed) || changed)
            context.Invalidate();
    }

    public void PointerReleased(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (Editor.Map is null)
            return;
        OnReleased(context, e, Editor.CellAt(e.World));
        if (!e.Properties.IsLeftButtonPressed && !e.Properties.IsRightButtonPressed)
            e.Source.Pointer.Capture(null);
        e.Handled = true;
        context.Invalidate();
    }

    public void PointerExited(ViewportToolContext context)
    {
        if (IsOperationInProgress)
            return;
        Editor.HoveredCell = null;
        context.Invalidate();
    }

    public bool KeyDown(ViewportToolContext context, KeyEventArgs e)
    {
        if (Editor.Map is null || Editor.IsBusy)
            return false;
        if (OnKeyDown(context, e))
            return true;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        switch (e.Key)
        {
            case Key.C when ctrl && !Editor.Selection.IsEmpty:
                Editor.Copy();
                return true;
            case Key.X when ctrl && !Editor.Selection.IsEmpty:
                Editor.Cut();
                return true;
            case Key.V when ctrl:
                Paste(context);
                return true;
            case Key.Delete or Key.Back when !Editor.Selection.IsEmpty && !IsOperationInProgress:
                Editor.DeleteSelection();
                return true;
            case Key.Escape when !IsOperationInProgress && !Editor.Selection.IsEmpty:
                Editor.ClearSelection();
                return true;
        }

        if (!TurnsBrush || ctrl || (e.KeyModifiers & KeyModifiers.Alt) != 0)
            return false;
        var steps = Editor.RotationSteps;
        switch (e.Key)
        {
            case Key.Z when shift:
                Editor.Brush.RotateCounterClockwise(steps);
                return true;
            case Key.Z:
                Editor.Brush.RotateClockwise(steps);
                return true;
            case Key.X:
                Editor.Brush.FlipHorizontal(steps);
                return true;
            case Key.Y:
                Editor.Brush.FlipVertical(steps);
                return true;
            default:
                return false;
        }
    }

    public void Render(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.Map is null)
            return;
        var colors = TileOverlay.Palette;
        RenderBackground(context, drawing);
        if (!Editor.Selection.IsEmpty)
        {
            Overlay.DrawCells(drawing, Editor.Selection, colors.SelectionFill, colors.SelectionShadowPen);
            Overlay.DrawCells(drawing, Editor.Selection, null, colors.SelectionPen);
        }

        RenderPreview(context, drawing);
    }

    public Control? CreateOptionsView() => _options ??= new TileToolOptionsBar { DataContext = new TileToolOptionsViewModel(Editor, this) };

    public virtual void Cancel(ViewportToolContext context)
    {
    }

    /// <summary>Copies the tile under <paramref name="cell"/> into the brush, preferring the active layer.</summary>
    protected bool Pick(GridCoord cell)
    {
        if (Editor.Map is not { } map || TilePicker.Pick(map, cell, Editor.ActiveTileLayer) is not { } picked)
            return false;
        Editor.Brush.Pick(picked.Tile);
        Editor.Brush.Stamp = null;
        return true;
    }

    /// <summary>Places the copied tiles with the stamp tool, whose preview follows the pointer.</summary>
    protected static void Paste(ViewportToolContext context)
    {
        var tools = context.Services.GetRequiredService<ToolManager>();
        if (tools.ActiveTool is not StampTool)
            tools.Select(StampTool.ToolId);
    }

    protected static void ReturnToPreviousTool(ViewportToolContext context) => context.Services.GetRequiredService<ToolManager>().ReturnToPrevious();

    protected virtual void OnPressed(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
    }

    /// <summary>Handles a pointer move; returns whether the overlay must be redrawn.</summary>
    protected virtual bool OnMoved(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell, bool cellChanged) => false;

    protected virtual void OnReleased(ViewportToolContext context, ViewportPointerEventArgs e, GridCoord cell)
    {
    }

    protected virtual bool OnKeyDown(ViewportToolContext context, KeyEventArgs e) => false;

    /// <summary>Draws below the selection, such as collision cells or objects.</summary>
    protected virtual void RenderBackground(ViewportToolContext context, DrawingContext drawing)
    {
    }

    /// <summary>Draws the tool's preview; by default the outline of the hovered cell.</summary>
    protected virtual void RenderPreview(ViewportToolContext context, DrawingContext drawing)
    {
        if (Editor.HoveredCell is { } cell)
            Overlay.DrawCell(drawing, cell, null, TileOverlay.Palette.HoverPen);
    }
}
