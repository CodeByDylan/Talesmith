using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Talesmith.Mathematics;

namespace Talesmith.Runtime.Serialization;

/// <summary>The text forms of values in scene, prefab and other authoring files.</summary>
/// <remarks>Colors are "#RRGGBB", or "#AARRGGBB" when not opaque, as Color formats them; guids are 32 hex digits; vectors are [x, y].</remarks>
public static class JsonFormats
{
    public static string FormatColor(Color color) => color.ToString();

    public static string FormatGuid(Guid guid) => guid.ToString("N");

    /// <summary>Reads a number from a JSON value, accepting numbers of any CLR type and numeric strings.</summary>
    /// <exception cref="FormatException">The node is not a number.</exception>
    public static double GetNumber(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<double>(out var d))
                return d;
            if (value.TryGetValue<float>(out var f))
                return f;
            if (value.TryGetValue<int>(out var i))
                return i;
            if (value.TryGetValue<long>(out var l))
                return l;
            if (value.TryGetValue<decimal>(out var m))
                return (double)m;
            if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number)
                return element.GetDouble();
            if (TryGetString(value, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            if (value.GetValueKind() == JsonValueKind.Number &&
                double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
        }

        throw new FormatException($"Expected a number but found {Describe(node)}.");
    }

    /// <summary>Reads an integer exactly when the value is integral, avoiding the precision loss of doubles for large values.</summary>
    /// <exception cref="FormatException">The node is not a number.</exception>
    public static long GetInteger(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out var l))
                return l;
            if (value.TryGetValue<int>(out var i))
                return i;
            if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out l))
                return l;
        }

        return (long)Math.Round(GetNumber(node));
    }

    public static bool GetBoolean(JsonNode node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var b))
                return b;
            if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return element.GetBoolean();
            if (TryGetString(value, out var text) && bool.TryParse(text, out b))
                return b;
        }

        throw new FormatException($"Expected true or false but found {Describe(node)}.");
    }

    public static string GetString(JsonNode node)
    {
        if (node is JsonValue value && TryGetString(value, out var text))
            return text;
        throw new FormatException($"Expected a string but found {Describe(node)}.");
    }

    public static JsonArray WriteVector2(Vector2 value) => [value.X, value.Y];

    public static Vector2 ReadVector2(JsonNode node)
    {
        if (node is JsonArray { Count: 2 } array && array[0] is { } x && array[1] is { } y)
            return new Vector2((float)GetNumber(x), (float)GetNumber(y));
        if (node is JsonObject obj && obj["x"] is { } ox && obj["y"] is { } oy)
            return new Vector2((float)GetNumber(ox), (float)GetNumber(oy));
        throw new FormatException($"Expected [x, y] but found {Describe(node)}.");
    }

    public static Color ReadColor(JsonNode node) =>
        Color.TryParse(GetString(node), out var color) ? color : throw new FormatException($"'{GetString(node)}' is not a color; use #RRGGBB or #AARRGGBB.");

    public static Guid ReadGuid(JsonNode node) =>
        Guid.TryParse(GetString(node), out var guid) ? guid : throw new FormatException($"'{GetString(node)}' is not a guid.");

    internal static string Describe(JsonNode? node) => node switch
    {
        null => "null",
        JsonObject => "an object",
        JsonArray => "an array",
        _ => node.ToJsonString()
    };

    private static bool TryGetString(JsonValue value, out string text)
    {
        if (value.TryGetValue<string>(out var s))
        {
            text = s;
            return true;
        }

        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.String)
        {
            text = element.GetString()!;
            return true;
        }

        text = "";
        return false;
    }
}
