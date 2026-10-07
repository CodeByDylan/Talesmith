using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;

namespace Talesmith.Runtime.Hosting;

/// <summary>How an exported game's executable starts its bundled game: <c>launcher.json</c> next to the executable, written by builds.</summary>
public sealed record LauncherSettings
{
    public const string FileName = "launcher.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>The game folder, relative to the launcher file.</summary>
    public string GameFolder { get; init; } = "game";

    /// <summary>The game's title, used for the folder of captures and logs.</summary>
    public string Title { get; init; } = "";

    public LogLevel LogLevel { get; init; } = LogLevel.Warning;

    /// <summary>Enables the F3, F4, F9 and F12 developer tools.</summary>
    public bool DeveloperTools { get; init; }

    /// <summary>Reads the launcher settings in a folder, or returns null when it has none.</summary>
    /// <exception cref="InvalidDataException">The file exists but is not valid.</exception>
    public static LauncherSettings? Find(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), JsonOptions) ?? new LauncherSettings();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not valid: {ex.Message}", ex);
        }
    }

    public void Save(string folder) => AtomicFile.WriteAllText(Path.Combine(folder, FileName), JsonSerializer.Serialize(this, JsonOptions));

    /// <summary>The absolute game folder for a launcher file in <paramref name="folder"/>.</summary>
    public string ResolveGameFolder(string folder) => Path.GetFullPath(Path.Combine(folder, GameFolder));
}
