namespace Talesmith.Runtime.Diagnostics;

/// <summary>Developer overlays drawn over the game, toggled by the host or by game code.</summary>
public sealed class DebugOptions
{
    /// <summary>Outlines cells around the mouse pointer.</summary>
    public bool ShowGrid { get; set; }

    /// <summary>Outlines every visible tile chunk.</summary>
    public bool ShowChunks { get; set; }

    /// <summary>Draws map objects: points, polygons and trigger areas.</summary>
    public bool ShowObjects { get; set; }

    public bool Any => ShowGrid || ShowChunks || ShowObjects;
}
