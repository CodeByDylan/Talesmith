using System.Text.Json;
using System.Text.Json.Serialization;
using Talesmith.Rendering;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Lighting;

/// <summary>A scene's lighting quality, lit layers and tile map shadows, saved in its environment under <c>"lighting"</c>.</summary>
/// <remarks>Ambient light is saved in <see cref="SceneEnvironment.AmbientLight"/> and <see cref="SceneEnvironment.AmbientIntensity"/>; scenes
/// without a <c>"lighting"</c> entry use the <see cref="LightingEnvironment"/> defaults.</remarks>
public sealed record SceneLightingSettings
{
    /// <summary>The key in <see cref="SceneEnvironment.Extra"/>.</summary>
    public const string Key = "lighting";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LightingQuality Quality { get; init; } = LightingQuality.Medium;

    /// <summary>The limits used when <see cref="Quality"/> is <see cref="LightingQuality.Custom"/>; null uses <see cref="LightingQualitySettings.Medium"/>.</summary>
    public LightingQualitySettings? CustomQuality { get; init; }

    /// <inheritdoc cref="LightingEnvironment.LitLayerLimit"/>
    public int LitLayerLimit { get; init; } = RenderLayers.Overlay;

    /// <inheritdoc cref="LightingEnvironment.TileMapShadows"/>
    public bool TileMapShadows { get; init; } = true;

    /// <inheritdoc cref="LightingEnvironment.TileMapShadowLayer"/>
    public int TileMapShadowLayer { get; init; }

    /// <summary>Reads the settings of a scene environment; missing or unreadable settings give the defaults.</summary>
    public static SceneLightingSettings Read(SceneEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        if (environment.Extra?.TryGetValue(Key, out var element) != true || element.ValueKind != JsonValueKind.Object)
            return new SceneLightingSettings();
        try
        {
            return element.Deserialize<SceneLightingSettings>(Options) ?? new SceneLightingSettings();
        }
        catch (JsonException)
        {
            return new SceneLightingSettings();
        }
    }

    /// <summary>Stores the settings in a scene environment.</summary>
    public void Write(SceneEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.Extra ??= [];
        environment.Extra[Key] = JsonSerializer.SerializeToElement(this, Options);
    }

    /// <summary>Sets the quality, lit layers and tile map shadows of a lighting environment.</summary>
    public void ApplyTo(LightingEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environment.Quality = Quality;
        environment.CustomQuality = CustomQuality ?? LightingQualitySettings.Medium;
        environment.LitLayerLimit = LitLayerLimit;
        environment.TileMapShadows = TileMapShadows;
        environment.TileMapShadowLayer = Math.Clamp(TileMapShadowLayer, 0, 31);
    }
}
