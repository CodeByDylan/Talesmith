using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Input;

/// <summary>An action and its bindings, as stored in an <see cref="InputProfile"/>.</summary>
public sealed record InputActionDefinition(InputActionKind Kind, IReadOnlyList<InputBinding> Bindings);

/// <summary>A set of action bindings that can be saved to and loaded from human-editable JSON.</summary>
/// <remarks>
/// Format: <c>{"actions":{"Interact":{"kind":"button","bindings":[{"type":"key","key":"E"}]}}}</c>. Binding types are <c>key</c>
/// (<c>key</c>, optional <c>modifiers</c> such as <c>"Control, Shift"</c>), <c>mouse</c> (<c>button</c>), <c>axis</c>
/// (<c>negative</c>, <c>positive</c>) and <c>vector</c> (<c>up</c>, <c>down</c>, <c>left</c>, <c>right</c>). Keys are
/// <see cref="Key"/> names or the aliases accepted by <see cref="KeyNames"/>. Loading throws <see cref="JsonException"/> on invalid data.
/// </remarks>
public sealed class InputProfile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowOutOfOrderMetadataProperties = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters =
        {
            new KeyJsonConverter(),
            new JsonStringEnumConverter<InputActionKind>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<KeyModifiers>(),
            new JsonStringEnumConverter<MouseButton>()
        }
    };

    /// <summary>Actions by name (case-insensitive).</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, InputActionDefinition> Actions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static InputProfile FromJson(string json) =>
        JsonSerializer.Deserialize<InputProfile>(json, JsonOptions) ?? throw new JsonException("The input profile is empty.");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static InputProfile Load(string path) => FromJson(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, ToJson());

    private sealed class KeyJsonConverter : JsonConverter<Key>
    {
        public override Key Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var name = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
            return name is not null && KeyNames.TryParse(name, out var key) ? key : throw new JsonException($"Unknown key '{name}'.");
        }

        public override void Write(Utf8JsonWriter writer, Key value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
