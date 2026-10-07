using Avalonia.Media;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>A viewport tool that works on the cells of a grid of its own, such as the tile tools on the edited tile map.</summary>
/// <remarks>While the tool is active and has a grid, the viewport shows that grid in place of its square one, shown or hidden on its own; see
/// <see cref="ViewportGrid"/>.</remarks>
internal interface IViewportGridTool : IViewportTool
{
    /// <summary>Whether the tool has a grid now, such as while a map is edited.</summary>
    bool HasGrid { get; }

    /// <summary>The grid's name on its toggle, such as "Tile grid".</summary>
    string GridName { get; }

    /// <summary>The icon of the grid's toggle, which may follow the shape of the cells.</summary>
    Geometry GridIcon { get; }

    /// <summary>Draws the grid over the view in lines of <paramref name="color"/>.</summary>
    void RenderGrid(ViewportToolContext context, DrawingContext drawing, Color color);
}
