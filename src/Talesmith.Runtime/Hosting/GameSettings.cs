using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Talesmith.Assets.Json;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Runtime.Hosting;

/// <summary>Which render backend a host should use.</summary>
public enum RendererPreference
{
    /// <summary>Vulkan when a device is available, otherwise Skia.</summary>
    Auto,
    Vulkan,
    Skia
}

/// <summary>A game's settings, read from <c>assets/config/game.json</c>.</summary>
public sealed record GameSettings
{
    public const string FileName = "config/game.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new ViewScaleModeJsonConverter(), new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new ColorJsonConverter(), new SceneRequestJsonConverter() }
    };

    public string Title { get; init; } = "Talesmith Game";

    public int WindowWidth { get; init; } = 1280;

    public int WindowHeight { get; init; } = 800;

    /// <summary>The size the game is designed for and how it scales to the window; without a <c>view</c> section a larger window shows more.</summary>
    public ViewSettings View { get; init; } = ViewSettings.Unscaled;

    public RendererPreference Renderer { get; init; } = RendererPreference.Auto;

    /// <summary>Fixed simulation steps per second.</summary>
    public int FixedUpdateRate { get; init; } = 60;

    /// <summary>The most fixed steps run in one frame before time is dropped.</summary>
    public int MaxFixedStepsPerFrame { get; init; } = 5;

    public TextureFilter TextureFilter { get; init; } = TextureFilter.Linear;

    /// <summary>The color behind everything when a scene sets none.</summary>
    public Color ClearColor { get; init; } = new(14, 15, 18);

    /// <summary>The scene loaded at startup: a request object, a scene name, or the path of a <c>.tscene</c> file.</summary>
    public SceneRequest StartScene { get; init; } = new("map");

    /// <summary>What the game shows while it starts and while a scene takes long to load.</summary>
    public LoadingScreenSettings LoadingScreen { get; init; } = new();

    /// <summary>The input profile applied at startup, relative to the asset root.</summary>
    public string InputProfile { get; init; } = "config/input.json";

    /// <summary>Folder of plugins relative to the asset root.</summary>
    public string PluginsFolder { get; init; } = "plugins";

    /// <summary>Runs one frame per display refresh; when false, frames run as fast as <see cref="MaxFramesPerSecond"/> allows.</summary>
    public bool VSync { get; init; } = true;

    /// <summary>The most frames per second, or 0 for no limit.</summary>
    public int MaxFramesPerSecond { get; init; }

    /// <summary>Stops game time while the window is in the background, such as when the player switches to another program.</summary>
    public bool PauseWhenInactive { get; init; }

    public bool ShowPerformanceOverlay { get; init; }

    /// <summary>Loads settings from the asset root, or defaults when the file is missing.</summary>
    /// <exception cref="InvalidDataException">The file exists but is not valid.</exception>
    public static GameSettings Load(string assetRoot)
    {
        var path = Path.Combine(assetRoot, FileName);
        if (!File.Exists(path))
            return new GameSettings();
        try
        {
            using var stream = File.OpenRead(path);
            var settings = JsonSerializer.Deserialize<GameSettings>(stream, JsonOptions) ?? new GameSettings();
            settings = settings with { View = settings.View ?? ViewSettings.Unscaled, LoadingScreen = settings.LoadingScreen ?? new LoadingScreenSettings() };
            return settings.View.FindError() is { } error ? throw new InvalidDataException($"{path} is not valid: {error}") : settings;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not valid: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a view scale mode by name, rejecting unknown names and numbers with a message that lists the known modes.</summary>
    private sealed class ViewScaleModeJsonConverter : JsonConverter<ViewScaleMode>
    {
        public override ViewScaleMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : Encoding.UTF8.GetString(reader.ValueSpan);
            foreach (var mode in Enum.GetValues<ViewScaleMode>())
            {
                if (string.Equals(text, mode.ToString(), StringComparison.OrdinalIgnoreCase))
                    return mode;
            }

            throw new JsonException($"\"{text}\" is not a view scale mode; use fit, expand, crop or none.");
        }

        public override void Write(Utf8JsonWriter writer, ViewScaleMode value, JsonSerializerOptions options) =>
            writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }

    /// <summary>Reads { "name", "parameters" }, a scene name, or a <c>.tscene</c> path, which runs in the "scene" scene.</summary>
    private sealed class SceneRequestJsonConverter : JsonConverter<SceneRequest>
    {
        public override SceneRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()!;
                return text.EndsWith(".tscene", StringComparison.OrdinalIgnoreCase) ? DocumentScene.ForPath(text) : new SceneRequest(text);
            }

            if (JsonNode.Parse(ref reader) is not JsonObject request || request["name"]?.GetValue<string>() is not { } name)
                throw new JsonException("A start scene is a scene name, a .tscene path, or an object with a name and parameters.");
            var parameters = new Dictionary<string, string>();
            if (request["parameters"] is JsonObject values)
            {
                foreach (var (key, value) in values)
                {
                    if (value is not null)
                        parameters[key] = value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : value.ToJsonString();
                }
            }

            return new SceneRequest(name, parameters);
        }

        public override void Write(Utf8JsonWriter writer, SceneRequest value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("name", value.Name);
            writer.WriteStartObject("parameters");
            foreach (var (key, parameter) in value.Parameters ?? new Dictionary<string, string>())
                writer.WriteString(key, parameter);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
    }
}
