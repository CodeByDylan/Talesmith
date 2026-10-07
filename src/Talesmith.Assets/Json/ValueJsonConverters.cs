using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Json;

/// <summary>Writes an <see cref="AssetGuid"/> as 32 hex digits, or null when empty, and reads any form <see cref="Guid"/> accepts.</summary>
public sealed class AssetGuidJsonConverter : JsonConverter<AssetGuid>
{
    public override bool HandleNull => true;

    public override AssetGuid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return AssetGuid.Empty;
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("An asset guid must be a string of 32 hex digits.");
        var text = reader.GetString();
        if (string.IsNullOrEmpty(text))
            return AssetGuid.Empty;
        return AssetGuid.TryParse(text, CultureInfo.InvariantCulture, out var guid) ? guid : throw new JsonException($"'{text}' is not an asset guid.");
    }

    public override void Write(Utf8JsonWriter writer, AssetGuid value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.IsEmpty)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value.ToString());
    }
}

/// <summary>Writes a <see cref="Vector2"/> as <c>[x, y]</c> on one line and also reads <c>{ "x": …, "y": … }</c>.</summary>
public sealed class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float? x = null, y = null;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase))
                    x = JsonArrays.ReadFloat(ref reader, "[x, y]");
                else if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase))
                    y = JsonArrays.ReadFloat(ref reader, "[x, y]");
                else
                    reader.Skip();
            }

            return x is { } ox && y is { } oy ? new Vector2(ox, oy) : throw new JsonException("Expected [x, y].");
        }

        Span<float> values = stackalloc float[2];
        JsonArrays.ReadFloats(ref reader, values, "[x, y]");
        return new Vector2(values[0], values[1]);
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options) =>
        JsonArrays.WriteFloats(writer, [value.X, value.Y]);
}

/// <summary>Writes a <see cref="Color"/> as <c>"#RRGGBB"</c>, or <c>"#AARRGGBB"</c> when not opaque.</summary>
public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return Color.TryParse(text, out var color) ? color : throw new JsonException($"'{text}' is not a color; use #RRGGBB or #AARRGGBB.");
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

/// <summary>Writes a <see cref="Curve"/> as a list of keys <c>{ time, value, inTangent, outTangent, interpolation }</c>, leaving out defaults.</summary>
/// <remarks>Also reads a number as a constant curve and <c>{ "keys": [...] }</c>.</remarks>
public sealed class CurveJsonConverter : JsonConverter<Curve>
{
    public override Curve Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return Curve.Constant(reader.GetSingle());
        var node = JsonNode.Parse(ref reader);
        if (node is JsonObject wrapper)
            node = wrapper["keys"];
        if (node is not JsonArray array)
            throw new JsonException("A curve must be a list of keys.");
        var keys = new List<CurveKey>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject key || key["time"] is null || key["value"] is null)
                throw new JsonException("A curve key needs a time and a value.");
            var interpolation = key["interpolation"]?.GetValue<string>() is { } mode && Enum.TryParse<CurveInterpolation>(mode, true, out var parsed)
                ? parsed
                : CurveInterpolation.Smooth;
            keys.Add(new CurveKey(JsonArrays.Number(key["time"]), JsonArrays.Number(key["value"]), JsonArrays.Number(key["inTangent"]),
                JsonArrays.Number(key["outTangent"]), interpolation));
        }

        return new Curve(keys);
    }

    public override void Write(Utf8JsonWriter writer, Curve value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartArray();
        foreach (var key in value.Keys)
        {
            writer.WriteStartObject();
            writer.WriteNumber("time", key.Time);
            writer.WriteNumber("value", key.Value);
            if (key.InTangent != 0)
                writer.WriteNumber("inTangent", key.InTangent);
            if (key.OutTangent != 0)
                writer.WriteNumber("outTangent", key.OutTangent);
            if (key.Interpolation != CurveInterpolation.Smooth)
                writer.WriteString("interpolation", JsonNamingPolicy.CamelCase.ConvertName(key.Interpolation.ToString()));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}

/// <summary>Writes a <see cref="Gradient"/> as a list of stops <c>{ position, color }</c>.</summary>
/// <remarks>Also reads a color as a solid gradient and <c>{ "stops": [...] }</c>.</remarks>
public sealed class GradientJsonConverter : JsonConverter<Gradient>
{
    public override Gradient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return Gradient.Solid(ReadColor(reader.GetString()));
        var node = JsonNode.Parse(ref reader);
        if (node is JsonObject wrapper)
            node = wrapper["stops"];
        if (node is not JsonArray array)
            throw new JsonException("A gradient must be a list of stops.");
        var stops = new List<GradientStop>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject stop || stop["position"] is null || stop["color"] is not JsonValue color)
                throw new JsonException("A gradient stop needs a position and a color.");
            stops.Add(new GradientStop(JsonArrays.Number(stop["position"]), ReadColor(color.GetValue<string>())));
        }

        return new Gradient(stops);
    }

    public override void Write(Utf8JsonWriter writer, Gradient value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartArray();
        foreach (var stop in value.Stops)
        {
            writer.WriteStartObject();
            writer.WriteNumber("position", stop.Position);
            writer.WriteString("color", stop.Color.ToString());
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static Color ReadColor(string? text) =>
        Color.TryParse(text, out var color) ? color : throw new JsonException($"'{text}' is not a color; use #RRGGBB or #AARRGGBB.");
}

internal sealed class Vector4JsonConverter : JsonConverter<Vector4>
{
    public override Vector4 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Span<float> values = stackalloc float[4];
        JsonArrays.ReadFloats(ref reader, values, "[x, y, z, w]");
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, Vector4 value, JsonSerializerOptions options) =>
        JsonArrays.WriteFloats(writer, [value.X, value.Y, value.Z, value.W]);
}

internal sealed class Rect2JsonConverter : JsonConverter<Rect2>
{
    public override Rect2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        Span<float> values = stackalloc float[4];
        JsonArrays.ReadFloats(ref reader, values, "[x, y, width, height]");
        return new Rect2(values[0], values[1], values[2], values[3]);
    }

    public override void Write(Utf8JsonWriter writer, Rect2 value, JsonSerializerOptions options) =>
        JsonArrays.WriteFloats(writer, [value.X, value.Y, value.Width, value.Height]);
}

internal static class JsonArrays
{
    public static void ReadFloats(ref Utf8JsonReader reader, scoped Span<float> values, string shape)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected an array {shape}.");

        var count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (count == values.Length)
                throw new JsonException($"Expected an array {shape}.");
            values[count++] = ReadFloat(ref reader, shape);
        }

        if (count != values.Length)
            throw new JsonException($"Expected an array {shape}.");
    }

    public static float ReadFloat(ref Utf8JsonReader reader, string shape) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetSingle(),
        JsonTokenType.String when float.TryParse(reader.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => throw new JsonException($"Expected numbers in {shape}.")
    };

    public static float Number(JsonNode? node) => node is null ? 0 : node.GetValue<float>();

    /// <summary>Writes the numbers as one raw <c>[a, b, …]</c> so short lists stay on one line in indented files.</summary>
    public static void WriteFloats(Utf8JsonWriter writer, ReadOnlySpan<float> values)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var text = new StringBuilder("[");
        for (var i = 0; i < values.Length; i++)
        {
            if (!float.IsFinite(values[i]))
                throw new JsonException($"{values[i]} cannot be written to JSON.");
            if (i > 0)
                text.Append(", ");
            text.Append(values[i].ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteRawValue(text.Append(']').ToString(), skipInputValidation: true);
    }
}
