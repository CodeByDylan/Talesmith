using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Talesmith.Lighting;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Lighting;

/// <summary>A named look for a scene: ambient light, quality and lit layers, saved as a <c>.tlighting</c> file.</summary>
/// <remarks>
/// <code>{ "version": 1, "name": "Night", "ambientColor": "#3A4A80", "ambientIntensity": 0.25, "lighting": { "quality": "high", … } }</code>
/// The <c>"lighting"</c> object has the shape of <see cref="SceneLightingSettings"/>; a preset without it leaves those settings unchanged.
/// </remarks>
public sealed record LightingPreset(string Name, Color AmbientColor, float AmbientIntensity, SceneLightingSettings? Lighting = null)
{
    public const string Extension = ".tlighting";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Presets that ship with the editor.</summary>
    public static IReadOnlyList<LightingPreset> BuiltIn { get; } =
    [
        new("Day", Color.White, 1),
        new("Golden hour", Color.Parse("#FFD6A0"), 0.85f),
        new("Overcast", Color.Parse("#C9D2DE"), 0.75f),
        new("Dusk", Color.Parse("#8C7BC9"), 0.5f),
        new("Night", Color.Parse("#3D4C85"), 0.28f),
        new("Dungeon", Color.Parse("#2C2340"), 0.12f)
    ];

    /// <summary>The scene environment with this preset's ambient light and lighting settings.</summary>
    public SceneEnvironment ApplyTo(SceneEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var copy = environment.Clone();
        copy.AmbientLight = AmbientColor;
        copy.AmbientIntensity = AmbientIntensity;
        Lighting?.Write(copy);
        return copy;
    }

    /// <summary>A preset of a scene's current ambient light and lighting settings.</summary>
    public static LightingPreset From(string name, SceneEnvironment environment) =>
        new(name, environment.AmbientLight, environment.AmbientIntensity, SceneLightingSettings.Read(environment));

    public string Serialize()
    {
        var json = new JsonObject
        {
            ["version"] = CurrentVersion,
            ["name"] = Name,
            ["ambientColor"] = AmbientColor.ToString(),
            ["ambientIntensity"] = AmbientIntensity
        };
        if (Lighting is not null)
            json["lighting"] = JsonSerializer.SerializeToNode(Lighting, Options);
        return json.ToJsonString(Options);
    }

    /// <exception cref="JsonException">The text is not a lighting preset.</exception>
    public static LightingPreset Deserialize(string text)
    {
        var json = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("A lighting preset must be a JSON object.");
        if (json["version"]?.GetValue<int>() is > CurrentVersion)
            throw new JsonException("The lighting preset was saved by a newer version.");
        var color = Color.TryParse(json["ambientColor"]?.GetValue<string>(), out var parsed) ? parsed : Color.White;
        var lighting = json["lighting"] is JsonObject settings ? settings.Deserialize<SceneLightingSettings>(Options) : null;
        return new LightingPreset(json["name"]?.GetValue<string>() ?? "Lighting", color, json["ambientIntensity"]?.GetValue<float>() ?? 1, lighting);
    }
}
