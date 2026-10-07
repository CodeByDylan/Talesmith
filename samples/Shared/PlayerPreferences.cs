using System.Text.Json;

namespace Talesmith.Samples.Shared;

/// <summary>Settings the player chooses in the menu, kept in the user's application data between sessions.</summary>
public sealed record PlayerPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public bool Fullscreen { get; init; }

    public bool VSync { get; init; } = true;

    /// <summary>The frame-rate limit, or 0 for none.</summary>
    public int MaxFramesPerSecond { get; init; }

    public bool ShowFrameRate { get; init; }

    public float MasterVolume { get; init; } = 1;

    public float MusicVolume { get; init; } = 0.8f;

    public float EffectsVolume { get; init; } = 1;

    /// <summary>Where a game's preferences are kept: a folder named after the game's title in the user's application data.</summary>
    public static string FilePathFor(string gameTitle)
    {
        var folder = string.Concat(gameTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Talesmith", folder.Length > 0 ? folder : "Game", "settings.json");
    }

    /// <summary>Loads saved preferences, or returns null when there are none or they cannot be read.</summary>
    public static PlayerPreferences? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<PlayerPreferences>(stream, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, this, JsonOptions);
    }
}
