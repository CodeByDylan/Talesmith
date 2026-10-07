using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Talesmith.Assets;
using Talesmith.Assets.Json;
using Talesmith.Authoring;

namespace Talesmith.VFX.Presets;

/// <summary>A named, reusable <see cref="ParticleSettings"/>, saved as a <c>.tparticles</c> file.</summary>
public sealed class ParticlePreset(string name, ParticleSettings settings)
{
    public string Name { get; } = name;

    public ParticleSettings Settings { get; } = settings;
}

/// <summary>Reads and writes <c>.tparticles</c> presets: <c>{ "version": 1, "name": …, "settings": { … } }</c>.</summary>
/// <remarks>
/// Settings are written field by field with camel-case names and enum values, the same form scenes use for inline emitter settings.
/// Colors are <c>"#RRGGBB"</c> or <c>"#AARRGGBB"</c>, vectors <c>[x, y]</c>, ranges <c>{ "min", "max" }</c> and
/// <c>{ "from", "to" }</c>, and plugin modules <c>{ "type": name, "data": { … } }</c>. Missing fields keep their defaults, so older presets load in newer versions, and modules of
/// unknown types are kept as <see cref="UnknownParticleModule"/> and written back unchanged.
/// </remarks>
public static class ParticlePresetSerializer
{
    public const string Extension = ".tparticles";
    public const int CurrentVersion = 1;

    private static readonly ConditionalWeakTable<ParticleModuleRegistry, JsonSerializerOptions> OptionsByRegistry = new();

    public static string Serialize(ParticlePreset preset, ParticleModuleRegistry? modules = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ToJson(preset, modules).ToJsonString(Options(modules));
    }

    public static void Serialize(Stream stream, ParticlePreset preset, ParticleModuleRegistry? modules = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(preset);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        ToJson(preset, modules).WriteTo(writer, Options(modules));
    }

    /// <summary>Saves a preset, replacing the file only once the new contents are written completely.</summary>
    public static void Save(string path, ParticlePreset preset, ParticleModuleRegistry? modules = null) =>
        AtomicFile.Write(path, stream => Serialize(stream, preset, modules));

    /// <exception cref="JsonException">The text is not a valid preset.</exception>
    public static ParticlePreset Deserialize(string json, ParticleModuleRegistry? modules = null) =>
        FromJson(JsonNode.Parse(json) as JsonObject ?? throw new JsonException("A particle preset must be a JSON object."), modules);

    /// <exception cref="JsonException">The stream does not contain a valid preset.</exception>
    public static ParticlePreset Deserialize(Stream stream, ParticleModuleRegistry? modules = null) =>
        FromJson(JsonNode.Parse(stream) as JsonObject ?? throw new JsonException("A particle preset must be a JSON object."), modules);

    /// <summary>Writes settings as the JSON object stored in presets and scenes.</summary>
    public static JsonObject SettingsToJson(ParticleSettings settings, ParticleModuleRegistry? modules = null) =>
        (JsonObject)JsonSerializer.SerializeToNode(settings, Options(modules))!;

    /// <exception cref="JsonException">The object is not valid particle settings.</exception>
    public static ParticleSettings SettingsFromJson(JsonObject json, ParticleModuleRegistry? modules = null) =>
        json.Deserialize<ParticleSettings>(Options(modules)) ?? new ParticleSettings();

    /// <summary>Saves one module as <c>{ "type", "data" }</c>, the form used inside presets and scenes.</summary>
    public static JsonNode ModuleToJson(IParticleModule module, ParticleModuleRegistry modules) =>
        JsonSerializer.SerializeToNode(module, Options(modules))!;

    /// <summary>Reads a module saved by <see cref="ModuleToJson"/>; unregistered types load as <see cref="UnknownParticleModule"/>.</summary>
    public static IParticleModule ModuleFromJson(JsonNode node, ParticleModuleRegistry modules) =>
        node.Deserialize<IParticleModule>(Options(modules)) ?? throw new JsonException("A particle module must be an object.");

    internal static ParticleSettings CloneSettings(ParticleSettings settings, ParticleModuleRegistry modules) =>
        SettingsFromJson(SettingsToJson(settings, modules), modules);

    private static JsonObject ToJson(ParticlePreset preset, ParticleModuleRegistry? modules) => new()
    {
        ["version"] = CurrentVersion,
        ["name"] = preset.Name,
        ["settings"] = SettingsToJson(preset.Settings, modules)
    };

    private static ParticlePreset FromJson(JsonObject json, ParticleModuleRegistry? modules)
    {
        var version = json["version"]?.GetValue<int>() ?? CurrentVersion;
        if (version > CurrentVersion)
            throw new JsonException($"The preset was saved by a newer version (format {version}); this version reads up to {CurrentVersion}.");
        var name = json["name"]?.GetValue<string>() ?? "Particles";
        var settings = json["settings"] is JsonObject data ? SettingsFromJson(data, modules) : new ParticleSettings();
        return new ParticlePreset(name, settings);
    }

    private static JsonSerializerOptions Options(ParticleModuleRegistry? modules)
    {
        modules ??= ParticleModuleRegistry.Empty;
        return OptionsByRegistry.GetValue(modules, CreateOptions);
    }

    private static JsonSerializerOptions CreateOptions(ParticleModuleRegistry modules)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { SkipTransient } }
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new Vector2JsonConverter());
        options.Converters.Add(new ColorJsonConverter());
        options.Converters.Add(new RangeConverter<MinMaxFloat>(RangeJson.ReadFloat, RangeJson.Write));
        options.Converters.Add(new RangeConverter<MinMaxColor>(RangeJson.ReadColor, RangeJson.Write));
        options.Converters.Add(new CurveJsonConverter());
        options.Converters.Add(new GradientJsonConverter());
        options.Converters.Add(new ModuleConverter(modules));
        options.MakeReadOnly();
        return options;
    }

    private static void SkipTransient(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object)
            return;
        for (var i = info.Properties.Count - 1; i >= 0; i--)
        {
            var property = info.Properties[i];
            if (property.AttributeProvider?.IsDefined(typeof(TransientAttribute), inherit: true) == true)
                info.Properties.RemoveAt(i);
        }
    }

    private sealed class RangeConverter<T>(Func<JsonNode, T> read, Func<T, JsonObject> write) : JsonConverter<T>
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var node = JsonNode.Parse(ref reader) ?? throw new JsonException($"A {typeof(T).Name} cannot be null.");
            try
            {
                return read(node);
            }
            catch (FormatException ex)
            {
                throw new JsonException(ex.Message, ex);
            }
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => write(value).WriteTo(writer, options);
    }

    private sealed class ModuleConverter(ParticleModuleRegistry modules) : JsonConverter<IParticleModule>
    {
        public override IParticleModule Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var node = JsonNode.Parse(ref reader) as JsonObject ?? throw new JsonException("A particle module must be an object.");
            var typeName = node["type"]?.GetValue<string>() ?? throw new JsonException("A particle module needs a \"type\".");
            var data = node["data"] as JsonObject ?? [];
            if (!modules.TryGetType(typeName, out var registration))
                return new UnknownParticleModule(typeName, (JsonObject)data.DeepClone());
            return (IParticleModule)(data.Deserialize(registration.Type, options) ?? Activator.CreateInstance(registration.Type)!);
        }

        public override void Write(Utf8JsonWriter writer, IParticleModule value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            if (value is UnknownParticleModule unknown)
            {
                writer.WriteString("type", unknown.TypeName);
                writer.WritePropertyName("data");
                unknown.Data.WriteTo(writer, options);
            }
            else
            {
                if (!modules.TryGetName(value.GetType(), out var typeName))
                    throw new JsonException($"The particle module {value.GetType().FullName} is not registered; register it with AddParticleModule.");
                writer.WriteString("type", typeName);
                writer.WritePropertyName("data");
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }

            writer.WriteEndObject();
        }
    }
}
