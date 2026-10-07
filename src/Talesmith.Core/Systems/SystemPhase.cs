namespace Talesmith.Systems;

/// <summary>When in a frame a system runs. Phases run in declaration order.</summary>
public enum SystemPhase
{
    /// <summary>Once per frame before simulation, for input and event processing.</summary>
    PreUpdate,

    /// <summary>Zero or more times per frame with a constant time step, for deterministic simulation.</summary>
    FixedUpdate,

    /// <summary>Once per frame with the frame's delta time, for gameplay that does not need a fixed step.</summary>
    Update,

    /// <summary>Once per frame after <see cref="Update"/>, for cameras and anything that follows other entities.</summary>
    LateUpdate,

    /// <summary>Once per frame just before rendering, to build draw lists from the final state.</summary>
    PreRender
}
