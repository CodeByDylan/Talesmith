namespace Talesmith.Assets.Maps;

/// <summary>What a map layer is for, which decides how the engine and the editor treat it.</summary>
public enum LayerRole
{
    /// <summary>Terrain drawn at the back.</summary>
    Ground,

    /// <summary>Details drawn over the ground.</summary>
    Decoration,

    /// <summary>Cells that block movement; non-empty cells are solid unless their tile defines its own collision shapes.</summary>
    Collision,

    /// <summary>Cells or areas that report when activators enter or leave them.</summary>
    Trigger,

    /// <summary>Free-form objects such as spawn points and placed images.</summary>
    Object,

    /// <summary>Walkability and costs for pathfinding.</summary>
    Navigation,

    /// <summary>Defined by a plugin; see the layer's properties.</summary>
    Custom
}
