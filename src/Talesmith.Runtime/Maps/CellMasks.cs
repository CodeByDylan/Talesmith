using System.Runtime.CompilerServices;
using Talesmith.Grids;
using Talesmith.Imaging;

namespace Talesmith.Runtime.Maps;

/// <summary>Caches one cell mask image per grid layout.</summary>
public static class CellMasks
{
    private static readonly ConditionalWeakTable<IGridLayout, ImageData> Masks = new();

    public static ImageData For(IGridLayout layout) => Masks.GetValue(layout, CellMask.Create);
}
