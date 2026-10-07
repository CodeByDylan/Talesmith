using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Assets.Json;

/// <summary>The JSON conventions of asset files such as .meta files, atlases and materials.</summary>
/// <remarks>
/// Properties are camelCase, enums are camelCase strings, nulls are omitted, comments and trailing commas are accepted, and vectors,
/// rectangles, colors and guids use the forms of the converters in this namespace.
/// </remarks>
public static class AssetJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Reads a JSON document of an asset file into <typeparamref name="T"/>.</summary>
    /// <exception cref="AssetException">The JSON is invalid or empty.</exception>
    public static T Deserialize<T>(ReadOnlySpan<byte> json, string description)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new AssetException($"{description} is empty.");
        }
        catch (JsonException ex)
        {
            throw new AssetException($"{description} is not valid: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a JSON document of an asset file into <typeparamref name="T"/>.</summary>
    /// <exception cref="AssetException">The JSON is invalid or empty.</exception>
    public static async Task<T> DeserializeAsync<T>(Stream stream, string description, CancellationToken cancellationToken = default)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken).ConfigureAwait(false)
                   ?? throw new AssetException($"{description} is empty.");
        }
        catch (JsonException ex)
        {
            throw new AssetException($"{description} is not valid: {ex.Message}", ex);
        }
    }

    public static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
            IndentSize = 2,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new Vector2JsonConverter());
        options.Converters.Add(new Vector4JsonConverter());
        options.Converters.Add(new Rect2JsonConverter());
        options.Converters.Add(new ColorJsonConverter());
        options.Converters.Add(new CurveJsonConverter());
        options.Converters.Add(new GradientJsonConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
