using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Talesmith.Authoring;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.VFX.Presets;

/// <summary>The saved form of <see cref="MinMaxFloat"/> and <see cref="MinMaxColor"/>, shared by presets and scenes.</summary>
/// <remarks>Ranges are written as <c>{ "min", "max" }</c> and <c>{ "from", "to" }</c>; a plain number or color, or <c>[min, max]</c>, also reads.</remarks>
internal static class RangeJson
{
    public static JsonObject Write(MinMaxFloat value) => new() { ["min"] = value.Min, ["max"] = value.Max };

    public static JsonObject Write(MinMaxColor value) =>
        new() { ["from"] = JsonFormats.FormatColor(value.From), ["to"] = JsonFormats.FormatColor(value.To) };

    /// <exception cref="FormatException">The JSON is not a range.</exception>
    public static MinMaxFloat ReadFloat(JsonNode node)
    {
        switch (node)
        {
            case JsonArray { Count: 2 } array:
                return new MinMaxFloat(Number(array[0]), Number(array[1]));
            case JsonObject data:
                var min = data["min"] ?? data["max"];
                var max = data["max"] ?? data["min"];
                return min is null ? default : new MinMaxFloat(Number(min), Number(max));
            default:
                return new MinMaxFloat(Number(node));
        }
    }

    /// <exception cref="FormatException">The JSON is not a color range.</exception>
    public static MinMaxColor ReadColor(JsonNode node)
    {
        if (node is not JsonObject data)
            return new MinMaxColor(JsonFormats.ReadColor(node));
        var from = data["from"] ?? data["to"];
        var to = data["to"] ?? data["from"];
        return from is null ? MinMaxColor.White : new MinMaxColor(JsonFormats.ReadColor(from), JsonFormats.ReadColor(to!));
    }

    /// <summary>Describes a field of a range for the inspector as the reflection serializer would.</summary>
    public static PropertyDescriptor Field<T>(string name, PropertyKind kind)
    {
        var field = typeof(T).GetField(name)!;
        return new PropertyDescriptor(JsonNamingPolicy.CamelCase.ConvertName(name), field.GetCustomAttribute<LabelAttribute>()?.Text ?? DisplayNames.FromIdentifier(name),
            kind, field.FieldType)
        { Tooltip = field.GetCustomAttribute<TooltipAttribute>()?.Text };
    }

    private static float Number(JsonNode? node) =>
        node is null ? throw new FormatException("Expected a number but found null.") : (float)JsonFormats.GetNumber(node);
}

/// <summary>Saves <see cref="MinMaxFloat"/> in scenes the way presets save it.</summary>
internal sealed class MinMaxFloatValueConverter : ValueConverter<MinMaxFloat>
{
    private static readonly PropertyDescriptor[] Children =
        [RangeJson.Field<MinMaxFloat>(nameof(MinMaxFloat.Min), PropertyKind.Number), RangeJson.Field<MinMaxFloat>(nameof(MinMaxFloat.Max), PropertyKind.Number)];

    public override PropertyKind Kind => PropertyKind.Object;

    public override JsonNode Write(MinMaxFloat value, ICaptureContext context) => RangeJson.Write(value);

    public override MinMaxFloat Read(JsonNode node, IInstantiationContext context) => RangeJson.ReadFloat(node);

    public override PropertyDescriptor Describe(PropertyDescriptor property) => property with { Kind = Kind, Children = Children };
}

/// <summary>Saves <see cref="MinMaxColor"/> in scenes the way presets save it.</summary>
internal sealed class MinMaxColorValueConverter : ValueConverter<MinMaxColor>
{
    private static readonly PropertyDescriptor[] Children =
        [RangeJson.Field<MinMaxColor>(nameof(MinMaxColor.From), PropertyKind.Color), RangeJson.Field<MinMaxColor>(nameof(MinMaxColor.To), PropertyKind.Color)];

    public override PropertyKind Kind => PropertyKind.Object;

    public override JsonNode Write(MinMaxColor value, ICaptureContext context) => RangeJson.Write(value);

    public override MinMaxColor Read(JsonNode node, IInstantiationContext context) => RangeJson.ReadColor(node);

    public override PropertyDescriptor Describe(PropertyDescriptor property) => property with { Kind = Kind, Children = Children };
}
