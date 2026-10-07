using System.Globalization;
using System.Text.Json.Nodes;

namespace Talesmith.Runtime.Serialization;

/// <summary>Reads and writes values inside component data by property path, as prefab overrides do.</summary>
/// <remarks>A path is property names separated by dots, with list items addressed by number, such as "tint" or "frames.2.duration".</remarks>
public static class JsonPaths
{
    /// <summary>Gets the value at a path, or null when any part of it is missing.</summary>
    public static JsonNode? Get(JsonObject root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(path);
        JsonNode? current = root;
        foreach (var range in path.AsSpan().Split('.'))
        {
            var segment = path[range];
            current = current switch
            {
                JsonObject obj => obj.TryGetPropertyValue(segment, out var child) ? child : null,
                JsonArray array when TryIndex(segment, out var index) && index < array.Count => array[index],
                _ => null
            };
            if (current is null)
                return null;
        }

        return current;
    }

    /// <summary>Sets the value at a path, creating missing objects on the way; a list index equal to the list's length appends.</summary>
    /// <exception cref="FormatException">The path passes through a value that is not an object or list, or a list index is out of range.</exception>
    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrEmpty(path);
        var segments = path.Split('.');
        JsonNode current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            var last = i == segments.Length - 1;
            switch (current)
            {
                case JsonObject obj when last:
                    obj[segment] = value;
                    return;
                case JsonObject obj:
                    if (obj[segment] is not { } next)
                        obj[segment] = next = TryIndex(segments[i + 1], out _) ? new JsonArray() : new JsonObject();
                    current = next;
                    break;
                case JsonArray array when TryIndex(segment, out var index) && index <= array.Count:
                    if (last)
                    {
                        if (index == array.Count)
                            array.Add(value);
                        else
                            array[index] = value;
                        return;
                    }

                    if (index == array.Count)
                        array.Add(TryIndex(segments[i + 1], out _) ? new JsonArray() : new JsonObject());
                    current = array[index] ?? throw new FormatException($"'{string.Join('.', segments[..(i + 1)])}' is null.");
                    break;
                default:
                    throw new FormatException($"'{string.Join('.', segments[..i])}' is not an object or list, or '{segment}' is out of range.");
            }
        }
    }

    private static bool TryIndex(string segment, out int index) =>
        int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index);
}
