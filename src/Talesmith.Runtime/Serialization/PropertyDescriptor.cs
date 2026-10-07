namespace Talesmith.Runtime.Serialization;

/// <summary>The kind of editor a property needs.</summary>
public enum PropertyKind
{
    Boolean,
    Integer,
    Number,
    String,
    Enum,
    Vector2,
    Color,
    Rect,
    Curve,
    Gradient,

    /// <summary>A guid of an asset file; see <see cref="PropertyDescriptor.AssetType"/>.</summary>
    Asset,

    /// <summary>The saved id of another entity in the same scene.</summary>
    Entity,

    /// <summary>A nested group of properties; see <see cref="PropertyDescriptor.Children"/>.</summary>
    Object,

    /// <summary>A list of values; see <see cref="PropertyDescriptor.Element"/>.</summary>
    List
}

/// <summary>Describes one saved, editable value of a component or script, with the hints the inspector uses to edit it.</summary>
/// <param name="Name">The key in the saved JSON object.</param>
/// <param name="ValueType">The runtime type of the value.</param>
public sealed record PropertyDescriptor(string Name, string Label, PropertyKind Kind, Type ValueType)
{
    public string? Tooltip { get; init; }

    /// <summary>A title shown above this property, starting a group.</summary>
    public string? Header { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    /// <summary>The increment for dragging and stepping numbers; 0 lets the editor choose.</summary>
    public double Step { get; init; }

    /// <summary>The number of lines for multi-line strings, or 0 for a single line.</summary>
    public int Lines { get; init; }

    /// <summary>For <see cref="PropertyKind.Enum"/>, the names that can be chosen.</summary>
    public IReadOnlyList<string> EnumNames { get; init; } = [];

    /// <summary>Whether a <see cref="PropertyKind.Enum"/> is a set of flags.</summary>
    public bool IsFlags { get; init; }

    /// <summary>For <see cref="PropertyKind.Asset"/>, the runtime asset type, such as a texture.</summary>
    public Type? AssetType { get; init; }

    /// <summary>For <see cref="PropertyKind.Asset"/>, the file extensions that can be chosen; empty allows any file the asset type accepts.</summary>
    public IReadOnlyList<string> AssetExtensions { get; init; } = [];

    /// <summary>For <see cref="PropertyKind.Object"/>, the nested properties.</summary>
    public IReadOnlyList<PropertyDescriptor> Children { get; init; } = [];

    /// <summary>For <see cref="PropertyKind.List"/>, describes each element.</summary>
    public PropertyDescriptor? Element { get; init; }

    /// <summary>For numbers in radians that the inspector shows in degrees.</summary>
    public bool IsAngle { get; init; }

    /// <summary>Saved but not shown in the inspector.</summary>
    public bool Hidden { get; init; }

    /// <summary>Whether the value may be null, which the inspector offers as unset.</summary>
    public bool IsNullable { get; init; }

    /// <summary>For strings naming a sprite or animation of a texture, where the names come from.</summary>
    public TextureItemSource? TextureItems { get; init; }

    /// <summary>For integers that are a layer or a mask of layers, which layers; see <see cref="IsLayerMask"/>.</summary>
    public Authoring.LayerSet? Layers { get; init; }

    /// <summary>Whether an integer with <see cref="Layers"/> is a bit mask of layers rather than one layer.</summary>
    public bool IsLayerMask { get; init; }

    /// <summary>For values that follow something else while unset, what they follow, such as "Follows the sprite".</summary>
    public string? AutoValue { get; init; }
}

/// <summary>What a texture item name refers to.</summary>
public enum TextureItemKind
{
    /// <summary>A name from <c>TextureAsset.Sprites</c>.</summary>
    Sprite,

    /// <summary>A name from <c>TextureAsset.Animations</c>.</summary>
    Animation
}

/// <summary>Where the inspector finds the names a texture item property can choose from.</summary>
/// <param name="TextureProperty">The name of the sibling property holding the texture.</param>
public sealed record TextureItemSource(string TextureProperty, TextureItemKind Kind);

/// <summary>Marks a string that names a sprite or animation of the texture in another field, so the inspector offers those names.</summary>
/// <param name="textureMember">The C# name of the field or property holding the texture.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TextureItemAttribute(string textureMember, TextureItemKind kind = TextureItemKind.Sprite) : Attribute
{
    public string TextureMember { get; } = textureMember;

    public TextureItemKind Kind { get; } = kind;
}
