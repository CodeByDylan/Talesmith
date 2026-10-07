namespace Talesmith.Assets.Maps;

/// <summary>A placed tile: tileset, tile and orientation packed into 32 bits.</summary>
/// <remarks>
/// Bits 0–17 hold the tile id, 18–27 the tileset id (0 means empty), 28–30 the clockwise rotation in steps and bit 31 a horizontal
/// flip applied before rotating. A step is 60° on hex grids and 90° on square grids. This matches the cell encoding of .hexy maps,
/// so chunk data is used as stored.
/// </remarks>
public readonly record struct TileCell(uint Raw)
{
    public const int MaxTileId = (1 << 18) - 1;
    public const int MaxTilesetId = (1 << 10) - 1;

    private const uint FlipBit = 0x8000_0000u;
    private const uint TransformBits = FlipBit | (7u << 28);

    public TileCell(int tilesetId, int tileId, int rotation = 0, bool flipX = false)
        : this((uint)(tileId & MaxTileId) | ((uint)(tilesetId & MaxTilesetId) << 18) | ((uint)(rotation & 7) << 28) | (flipX ? FlipBit : 0))
    {
    }

    public static TileCell Empty => default;

    public bool IsEmpty => TilesetId == 0;

    public int TileId => (int)(Raw & MaxTileId);

    public int TilesetId => (int)((Raw >> 18) & MaxTilesetId);

    /// <summary>Clockwise rotation in steps of the grid's <see cref="TileMap.RotationStepDegrees"/>.</summary>
    public int Rotation => (int)((Raw >> 28) & 7);

    public bool FlipX => (Raw & FlipBit) != 0;

    /// <summary>The same tile without rotation or flip, identifying the artwork only.</summary>
    public TileCell WithoutTransform => new(Raw & ~TransformBits);

    /// <summary>The same tile with another orientation; <paramref name="rotation"/> wraps into one turn of <paramref name="rotationSteps"/>.</summary>
    public TileCell WithTransform(int rotation, bool flipX, int rotationSteps)
    {
        if (IsEmpty)
            return this;
        ArgumentOutOfRangeException.ThrowIfLessThan(rotationSteps, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rotationSteps, 8);
        var wrapped = ((rotation % rotationSteps) + rotationSteps) % rotationSteps;
        return new TileCell((Raw & ~TransformBits) | ((uint)wrapped << 28) | (flipX ? FlipBit : 0));
    }

    /// <summary>Rotates the placed tile clockwise by whole steps.</summary>
    public TileCell Rotate(int clockwiseSteps, int rotationSteps) => WithTransform(Rotation + clockwiseSteps, FlipX, rotationSteps);

    /// <summary>Mirrors the placed tile across the vertical axis, as seen after its rotation.</summary>
    public TileCell FlipHorizontal(int rotationSteps) => WithTransform(-Rotation, !FlipX, rotationSteps);

    /// <summary>Mirrors the placed tile across the horizontal axis, as seen after its rotation.</summary>
    public TileCell FlipVertical(int rotationSteps) => WithTransform(rotationSteps / 2 - Rotation, !FlipX, rotationSteps);

    public override string ToString() => IsEmpty ? "empty" : $"{TilesetId}:{TileId}{(Rotation != 0 ? $" r{Rotation}" : string.Empty)}{(FlipX ? " flipped" : string.Empty)}";
}
