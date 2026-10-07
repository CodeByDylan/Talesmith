using Talesmith.Assets;
using Talesmith.Runtime.Serialization;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles.Fields;

/// <summary>Creates the editor for each property of particle settings from its description.</summary>
public sealed class ParticleFieldFactory(ParticleFieldContext context, IAssetCatalog? catalog)
{
    /// <summary>Creates fields for the children of an object, skipping hidden properties and <paramref name="skip"/>.</summary>
    public IReadOnlyList<ParticleField> CreateChildren(PropertyDescriptor parent, string path, params string[] skip)
    {
        var fields = new List<ParticleField>();
        foreach (var child in parent.Children)
        {
            if (child.Hidden || skip.Contains(child.Name))
                continue;
            if (Create(child, path.Length == 0 ? child.Name : $"{path}.{child.Name}") is { } field)
                fields.Add(field);
        }

        return fields;
    }

    public ParticleField? Create(PropertyDescriptor property, string path)
    {
        if (property.ValueType == typeof(MinMaxFloat))
            return new RangeValue(context, property, path);
        if (property.ValueType == typeof(MinMaxColor))
            return new ColorRangeValue(context, property, path);
        return property.Kind switch
        {
            PropertyKind.Number or PropertyKind.Integer => new NumberValue(context, property, path),
            PropertyKind.Boolean => new ToggleValue(context, property, path),
            PropertyKind.Enum => new ChoiceValue(context, property, path),
            PropertyKind.Vector2 => new VectorValue(context, property, path),
            PropertyKind.Color => new ColorValue(context, property, path),
            PropertyKind.String => new TextValue(context, property, path),
            PropertyKind.Curve => new CurveValue(context, property, path),
            PropertyKind.Gradient => new GradientValue(context, property, path),
            PropertyKind.Asset => new AssetValue(context, property, path, catalog),
            PropertyKind.List when property.Element is { } element => new ListValue(context, property, path, CreateItemFields, () => CreateItem(element)),
            _ => null
        };
    }

    private IReadOnlyList<ParticleField> CreateItemFields(string path, PropertyDescriptor element) =>
        element.Kind == PropertyKind.Object && element.ValueType != typeof(MinMaxFloat) && element.ValueType != typeof(MinMaxColor)
            ? CreateChildren(element, path)
            : Create(element with { Label = "" }, path) is { } field ? [field] : [];

    private System.Text.Json.Nodes.JsonNode? CreateItem(PropertyDescriptor element)
    {
        var type = element.ValueType;
        if (type.IsInterface || type.IsAbstract)
            return null;
        try
        {
            return context.Codec.Encode(Activator.CreateInstance(type)!, type);
        }
        catch (MissingMethodException)
        {
            return null;
        }
    }
}
