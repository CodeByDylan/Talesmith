using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Assets.Json;
using Talesmith.Ecs;
using Talesmith.Mathematics;

namespace Talesmith.Runtime.Serialization.Converters;

internal sealed class BooleanConverter : ValueConverter<bool>
{
    public override PropertyKind Kind => PropertyKind.Boolean;

    public override JsonNode Write(bool value, ICaptureContext context) => JsonValue.Create(value);

    public override bool Read(JsonNode node, IInstantiationContext context) => JsonFormats.GetBoolean(node);
}

internal sealed class IntegerConverter<T> : ValueConverter<T> where T : struct, IBinaryInteger<T>, IMinMaxValue<T>
{
    public override PropertyKind Kind => PropertyKind.Integer;

    public override JsonNode Write(T value, ICaptureContext context) =>
        typeof(T) == typeof(ulong) ? JsonValue.Create(ulong.CreateTruncating(value)) : JsonValue.Create(long.CreateTruncating(value));

    public override T Read(JsonNode node, IInstantiationContext context) => T.CreateSaturating(JsonFormats.GetInteger(node));

    public override PropertyDescriptor Describe(PropertyDescriptor property) =>
        property with { Kind = Kind, Min = double.CreateTruncating(T.MinValue), Max = double.CreateTruncating(T.MaxValue), Step = 1 };
}

internal sealed class SingleConverter : ValueConverter<float>
{
    public override PropertyKind Kind => PropertyKind.Number;

    public override JsonNode Write(float value, ICaptureContext context) => JsonValue.Create(value);

    public override float Read(JsonNode node, IInstantiationContext context) => (float)JsonFormats.GetNumber(node);
}

internal sealed class DoubleConverter : ValueConverter<double>
{
    public override PropertyKind Kind => PropertyKind.Number;

    public override JsonNode Write(double value, ICaptureContext context) => JsonValue.Create(value);

    public override double Read(JsonNode node, IInstantiationContext context) => JsonFormats.GetNumber(node);
}

internal sealed class DecimalConverter : ValueConverter<decimal>
{
    public override PropertyKind Kind => PropertyKind.Number;

    public override JsonNode Write(decimal value, ICaptureContext context) => JsonValue.Create(value);

    public override decimal Read(JsonNode node, IInstantiationContext context) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var d) ? d : (decimal)JsonFormats.GetNumber(node);
}

internal sealed class StringConverter : ValueConverter<string>
{
    public override PropertyKind Kind => PropertyKind.String;

    public override JsonNode? Write(string value, ICaptureContext context) => value is null ? null : JsonValue.Create(value);

    public override string Read(JsonNode node, IInstantiationContext context) => JsonFormats.GetString(node);
}

internal sealed class GuidConverter : ValueConverter<Guid>
{
    public override PropertyKind Kind => PropertyKind.String;

    public override JsonNode Write(Guid value, ICaptureContext context) => JsonValue.Create(JsonFormats.FormatGuid(value));

    public override Guid Read(JsonNode node, IInstantiationContext context) => JsonFormats.ReadGuid(node);
}

internal sealed class Vector2Converter : ValueConverter<Vector2>
{
    public override PropertyKind Kind => PropertyKind.Vector2;

    public override JsonNode Write(Vector2 value, ICaptureContext context) => JsonFormats.WriteVector2(value);

    public override Vector2 Read(JsonNode node, IInstantiationContext context) => JsonFormats.ReadVector2(node);
}

internal sealed class ColorConverter : ValueConverter<Color>
{
    public override PropertyKind Kind => PropertyKind.Color;

    public override JsonNode Write(Color value, ICaptureContext context) => JsonValue.Create(JsonFormats.FormatColor(value));

    public override Color Read(JsonNode node, IInstantiationContext context) => JsonFormats.ReadColor(node);
}

/// <summary>Saves rectangles as [x, y, width, height].</summary>
internal sealed class Rect2Converter : ValueConverter<Rect2>
{
    public override PropertyKind Kind => PropertyKind.Rect;

    public override JsonNode Write(Rect2 value, ICaptureContext context) => new JsonArray(value.X, value.Y, value.Width, value.Height);

    public override Rect2 Read(JsonNode node, IInstantiationContext context)
    {
        if (node is JsonArray { Count: 4 } array && array[0] is { } x && array[1] is { } y && array[2] is { } w && array[3] is { } h)
            return new Rect2((float)JsonFormats.GetNumber(x), (float)JsonFormats.GetNumber(y), (float)JsonFormats.GetNumber(w), (float)JsonFormats.GetNumber(h));
        throw new FormatException($"Expected [x, y, width, height] but found {JsonFormats.Describe(node)}.");
    }
}

internal sealed class CurveConverter : JsonValueConverter<Curve>
{
    public override PropertyKind Kind => PropertyKind.Curve;
}

internal sealed class GradientConverter : JsonValueConverter<Gradient>
{
    public override PropertyKind Kind => PropertyKind.Gradient;
}

/// <summary>Saves values in the form <see cref="AssetJson.Options"/> gives them.</summary>
internal abstract class JsonValueConverter<T> : ValueConverter<T>
{
    public override JsonNode? Write(T value, ICaptureContext context) => value is null ? null : JsonSerializer.SerializeToNode(value, AssetJson.Options);

    public override T Read(JsonNode node, IInstantiationContext context)
    {
        try
        {
            return node.Deserialize<T>(AssetJson.Options) ?? throw new FormatException($"Expected a {typeof(T).Name} but found null.");
        }
        catch (JsonException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }
}

/// <summary>Saves asset guids as 32 hex digits, or null when empty; the asset is not loaded with the scene.</summary>
internal sealed class AssetGuidConverter : ValueConverter<AssetGuid>
{
    public override PropertyKind Kind => PropertyKind.Asset;

    public override JsonNode? Write(AssetGuid value, ICaptureContext context) => value.IsEmpty ? null : JsonValue.Create(value.ToString());

    public override AssetGuid Read(JsonNode node, IInstantiationContext context) =>
        AssetGuid.TryParse(JsonFormats.GetString(node), CultureInfo.InvariantCulture, out var guid) ? guid : throw new FormatException($"'{JsonFormats.GetString(node)}' is not an asset guid.");
}

/// <summary>Saves entity references as the saved id of the entity in the same scene or prefab.</summary>
internal sealed class EntityConverter : ValueConverter<Entity>
{
    public override PropertyKind Kind => PropertyKind.Entity;

    public override JsonNode? Write(Entity value, ICaptureContext context) =>
        value.IsNull || context.GetEntityId(value) is var id && id == Guid.Empty ? null : JsonValue.Create(JsonFormats.FormatGuid(id));

    public override Entity Read(JsonNode node, IInstantiationContext context) => context.GetEntity(JsonFormats.ReadGuid(node));
}

/// <summary>Saves a reference to an asset of type <typeparamref name="T"/> as its guid; the asset is loaded before the scene is created.</summary>
/// <remarks>Register one per asset type that components refer to, such as <c>services.AddValueConverter&lt;AssetReferenceConverter&lt;ParticlePreset&gt;&gt;()</c>.</remarks>
public sealed class AssetReferenceConverter<T> : ValueConverter<T> where T : class
{
    public override PropertyKind Kind => PropertyKind.Asset;

    public override JsonNode? Write(T value, ICaptureContext context) =>
        value is null || context.GetGuid(value) is var guid && guid.IsEmpty ? null : JsonValue.Create(guid.ToString());

    public override T Read(JsonNode node, IInstantiationContext context) => context.GetAsset<T>(ReadGuid(node))!;

    public override PropertyDescriptor Describe(PropertyDescriptor property) => property with { Kind = Kind, AssetType = typeof(T), IsNullable = true };

    public override void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies)
    {
        if (ReadGuid(node) is { IsEmpty: false } guid)
            dependencies.Add(new AssetDependency(guid, typeof(T)));
    }

    internal static AssetGuid ReadGuid(JsonNode node) =>
        AssetGuid.TryParse(JsonFormats.GetString(node), CultureInfo.InvariantCulture, out var guid) ? guid : throw new FormatException($"'{JsonFormats.GetString(node)}' is not an asset guid.");
}

internal static class JsonNames
{
    /// <summary>Converts a C# member name to the camelCase form saved in files.</summary>
    public static string ToJson(string name) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(name);
}
