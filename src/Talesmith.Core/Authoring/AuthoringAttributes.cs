namespace Talesmith.Authoring;

/// <summary>Describes a component that can be added to entities in scenes and prefabs.</summary>
/// <param name="displayName">The name shown in the editor; defaults to the type name split into words.</param>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ComponentAttribute(string? displayName = null) : Attribute
{
    public string? DisplayName { get; } = displayName;

    /// <summary>The group in the Add Component menu, such as "Rendering" or "Physics".</summary>
    public string? Category { get; init; }

    /// <summary>A one-line explanation shown in the Add Component menu and inspector.</summary>
    public string? Description { get; init; }

    /// <summary>The name of an icon from the editor's icon set.</summary>
    public string? Icon { get; init; }

    /// <summary>Hides the component from the Add Component menu, for components that the engine adds itself.</summary>
    public bool Hidden { get; init; }
}

/// <summary>Limits a numeric field to a range; the inspector shows a slider when both ends are finite.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class RangeAttribute(double min, double max = double.PositiveInfinity) : Attribute
{
    public double Min { get; } = min;

    public double Max { get; } = max;

    /// <summary>The increment used when dragging or stepping the value; 0 lets the editor choose.</summary>
    public double Step { get; init; }
}

/// <summary>Explains a field in the inspector.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TooltipAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Renames a field in the inspector.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LabelAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Starts a titled group of fields in the inspector.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HeaderAttribute(string text) : Attribute
{
    public string Text { get; } = text;
}

/// <summary>Edits a string field in a multi-line text box.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MultilineAttribute(int lines = 3) : Attribute
{
    public int Lines { get; } = lines;
}

/// <summary>Saves the field but does not show it in the inspector.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HideInInspectorAttribute : Attribute;

/// <summary>Runtime state that is neither saved nor shown, such as caches and simulation buffers.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TransientAttribute : Attribute;

/// <summary>Restricts an asset reference field to files with these extensions, such as ".png".</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AssetFilterAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions { get; } = extensions;
}

/// <summary>Marks a value in radians that the inspector edits in degrees.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AngleAttribute : Attribute;

/// <summary>Saves a non-public field and shows it in the inspector, like a public field.</summary>
/// <remarks>Use <c>[field: SerializeField]</c> to save the backing field of an auto-property, such as one with a private setter.</remarks>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute;

/// <summary>Marks a value that follows something else while it is not set, such as a sprite's region following its named sprite.</summary>
/// <param name="description">What the value follows, as the inspector shows it, such as "Follows the sprite".</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AutoValueAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>A set of 32 layers that layer and layer mask fields refer to.</summary>
public enum LayerSet
{
    /// <summary>Collision layers, named in the project's physics settings.</summary>
    Physics,

    /// <summary>The layers shadow casters are on and lights choose from.</summary>
    ShadowCasters
}

/// <summary>Edits an integer as a bit mask of layers, one checkbox per layer.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LayerMaskAttribute(LayerSet layers = LayerSet.Physics) : Attribute
{
    public LayerSet Layers { get; } = layers;
}

/// <summary>Edits an integer as one layer, chosen by name.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LayerAttribute(LayerSet layers = LayerSet.Physics) : Attribute
{
    public LayerSet Layers { get; } = layers;
}
