using System.Text.Json.Nodes;
using Talesmith.Assets.Textures;
using Talesmith.Rendering;
using Talesmith.Runtime.Rendering;

namespace Talesmith.Runtime.Serialization.Converters;

/// <summary>Saves texture handles as the guid of their <see cref="TextureAsset"/>, which is loaded and uploaded when the scene is created.</summary>
/// <remarks>Textures that did not come from a texture asset, such as generated masks, are saved as null.</remarks>
public sealed class TextureConverter(TextureCache textures) : ValueConverter<Texture>
{
    public override PropertyKind Kind => PropertyKind.Asset;

    public override JsonNode? Write(Texture value, ICaptureContext context) =>
        !value.IsNone && textures.TryGetAsset(value, out var asset) && context.GetGuid(asset) is { IsEmpty: false } guid ? JsonValue.Create(guid.ToString()) : null;

    public override Texture Read(JsonNode node, IInstantiationContext context) =>
        ResolveAsset(node, context) is { } asset ? textures.Get(asset) : Texture.None;

    public override PropertyDescriptor Describe(PropertyDescriptor property) =>
        property with { Kind = Kind, AssetType = typeof(TextureAsset), IsNullable = true };

    public override void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies)
    {
        if (AssetReferenceConverter<TextureAsset>.ReadGuid(node) is { IsEmpty: false } guid)
            dependencies.Add(new AssetDependency(guid, typeof(TextureAsset)));
    }

    /// <summary>Gets the texture asset a saved texture value refers to, or null when it is missing.</summary>
    public static TextureAsset? ResolveAsset(JsonNode? node, IInstantiationContext context) =>
        node is null || AssetReferenceConverter<TextureAsset>.ReadGuid(node) is not { IsEmpty: false } guid ? null : context.GetAsset<TextureAsset>(guid);
}
