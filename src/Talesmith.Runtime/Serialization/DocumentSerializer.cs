using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Talesmith.Assets.Json;

namespace Talesmith.Runtime.Serialization;

/// <summary>The kinds of authoring documents, for migrations.</summary>
public enum DocumentKind
{
    Scene,
    Prefab
}

/// <summary>Upgrades a document from one format version to the next before it is read.</summary>
/// <remarks>Register migrations with dependency injection as <see cref="IDocumentMigration"/>; they run in version order.</remarks>
public interface IDocumentMigration
{
    DocumentKind Kind { get; }

    /// <summary>The version this migration reads; the document has version <c>FromVersion + 1</c> afterwards.</summary>
    int FromVersion { get; }

    /// <summary>Changes the document's JSON in place; the version field is updated by the caller.</summary>
    void Migrate(JsonObject document);
}

/// <summary>Reads and writes scene and prefab documents as indented, camelCase JSON with a stable property order.</summary>
/// <remarks>Unknown fields and unknown components are kept and written back unchanged. Short number lists such as vectors are written on one line.</remarks>
public sealed class DocumentSerializer
{
    private readonly Dictionary<(DocumentKind Kind, int Version), IDocumentMigration> _migrations = new();

    public DocumentSerializer(IEnumerable<IDocumentMigration> migrations)
    {
        foreach (var migration in migrations)
            _migrations[(migration.Kind, migration.FromVersion)] = migration;
    }

    /// <summary>A serializer without migrations.</summary>
    public static DocumentSerializer Default { get; } = new([]);

    /// <summary>The options documents are written with; use them for other authoring files to share their formats.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <exception cref="InvalidDataException">The document is not valid or was written by a newer version.</exception>
    public SceneDocument ReadScene(ReadOnlySpan<byte> utf8) => Read<SceneDocument>(utf8, DocumentKind.Scene, SceneDocument.CurrentVersion);

    public SceneDocument ReadScene(Stream stream) => ReadScene(ReadAll(stream));

    public SceneDocument ReadScene(string json) => ReadScene(Encoding.UTF8.GetBytes(json));

    /// <exception cref="InvalidDataException">The document is not valid or was written by a newer version.</exception>
    public PrefabDocument ReadPrefab(ReadOnlySpan<byte> utf8) => Read<PrefabDocument>(utf8, DocumentKind.Prefab, PrefabDocument.CurrentVersion);

    public PrefabDocument ReadPrefab(Stream stream) => ReadPrefab(ReadAll(stream));

    public PrefabDocument ReadPrefab(string json) => ReadPrefab(Encoding.UTF8.GetBytes(json));

    public static void Write(Stream stream, SceneDocument document) => JsonSerializer.Serialize(stream, document, Options);

    public static void Write(Stream stream, PrefabDocument document) => JsonSerializer.Serialize(stream, document, Options);

    public static string Write(SceneDocument document) => JsonSerializer.Serialize(document, Options);

    public static string Write(PrefabDocument document) => JsonSerializer.Serialize(document, Options);

    private T Read<T>(ReadOnlySpan<byte> utf8, DocumentKind kind, int currentVersion) where T : class
    {
        try
        {
            var version = PeekVersion(utf8);
            if (version > currentVersion)
                throw new InvalidDataException($"The {kind.ToString().ToLowerInvariant()} has format version {version}, but this version of Talesmith reads up to {currentVersion}. Update Talesmith to open it.");

            if (version == currentVersion)
                return JsonSerializer.Deserialize<T>(utf8, Options) ?? throw new InvalidDataException("The document is empty.");

            var root = JsonNode.Parse(utf8, documentOptions: DocumentOptions) as JsonObject ?? throw new InvalidDataException("The document is not a JSON object.");
            for (var v = version; v < currentVersion; v++)
            {
                if (_migrations.TryGetValue((kind, v), out var migration))
                    migration.Migrate(root);
                root["version"] = v + 1;
            }

            return root.Deserialize<T>(Options) ?? throw new InvalidDataException("The document is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The document is not valid JSON: {ex.Message}", ex);
        }
    }

    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>Finds the top-level "version" field without parsing the rest; documents without one are version 1.</summary>
    private static int PeekVersion(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new InvalidDataException("The document is not a JSON object.");
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isVersion = reader.ValueTextEquals("version"u8);
            reader.Read();
            if (isVersion)
                return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version) ? version : throw new InvalidDataException("The document's version is not a number.");
            reader.Skip();
        }

        return 1;
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new GuidConverter(),
                new ColorJsonConverter(),
                new Vector2JsonConverter()
            }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class GuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Guid.TryParse(reader.GetString(), out var guid) ? guid : throw new JsonException($"'{reader.GetString()}' is not a guid.");

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(JsonFormats.FormatGuid(value));
    }
}

/// <summary>Reads JSON objects as they are and writes them with short number lists on one line; applied to component data.</summary>
internal sealed class CompactJsonObjectConverter : JsonConverter<JsonObject>
{
    public override JsonObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException("Expected an object.");

    public override void Write(Utf8JsonWriter writer, JsonObject value, JsonSerializerOptions options) => CompactJson.Write(writer, value, options);
}

/// <summary>Reads JSON values as they are and writes them with short number lists on one line; applied to override values.</summary>
internal sealed class CompactJsonNodeConverter : JsonConverter<JsonNode>
{
    public override bool HandleNull => true;

    public override JsonNode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => JsonNode.Parse(ref reader);

    public override void Write(Utf8JsonWriter writer, JsonNode? value, JsonSerializerOptions options) => CompactJson.Write(writer, value, options);
}

/// <summary>Writes JSON nodes with short lists of numbers, such as vectors and rectangles, on one line.</summary>
internal static class CompactJson
{
    private const int MaxInlineNumbers = 4;

    public static void Write(Utf8JsonWriter writer, JsonNode? node, JsonSerializerOptions options)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (name, value) in obj)
                {
                    writer.WritePropertyName(name);
                    Write(writer, value, options);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array when IsShortNumberList(array):
                var buffer = new ArrayBufferWriter<byte>(32);
                buffer.Write("["u8);
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                        buffer.Write(", "u8);
                    buffer.Write(Encoding.UTF8.GetBytes(array[i]!.ToJsonString(options)));
                }

                buffer.Write("]"u8);
                writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                    Write(writer, item, options);
                writer.WriteEndArray();
                break;
            default:
                node.WriteTo(writer, options);
                break;
        }
    }

    private static bool IsShortNumberList(JsonArray array)
    {
        if (array.Count is 0 or > MaxInlineNumbers)
            return false;
        foreach (var item in array)
        {
            if (item is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
                return false;
        }

        return true;
    }
}
