using Microsoft.Extensions.Logging;

namespace Talesmith.Physics;

/// <summary>Source-generated log messages of the physics module.</summary>
internal static partial class PhysicsLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The collider of {Entity} is invalid and was skipped: {Reason}")]
    public static partial void InvalidCollider(this ILogger logger, string entity, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The collision polygon of tile {Tile} in tileset {Tileset} is invalid, so the whole cell is solid: {Reason}")]
    public static partial void InvalidTileCollision(this ILogger logger, int tileset, int tile, string reason);
}
