namespace Talesmith.Rendering;

/// <summary>How drawn pixels combine with what is already on screen.</summary>
public enum BlendMode
{
    /// <summary>Normal transparency.</summary>
    Alpha,

    /// <summary>Adds light, for glows, fire and magic.</summary>
    Additive,

    /// <summary>Darkens by multiplying, for shadows and tinting.</summary>
    Multiply,

    /// <summary>Ignores transparency and overwrites the destination.</summary>
    Opaque
}
