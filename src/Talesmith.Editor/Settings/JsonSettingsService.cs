using System.Text.Json;
using System.Text.Json.Serialization;

namespace Talesmith.Editor.Settings;

/// <summary>Stores settings as JSON in the user's application data folder.</summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly string? _path;

    /// <param name="path">The settings file; null keeps settings in memory only, as tests and screenshots do.</param>
    public JsonSettingsService(string? path)
    {
        _path = path;
        Current = path is null ? new EditorSettings() : Load(path);
    }

    /// <summary>The settings file in the user's application data folder.</summary>
    public static string DefaultPath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Talesmith", "settings.json");

    public EditorSettings Current { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Reads settings without creating a service, for use before the application starts.</summary>
    public static EditorSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.EditorSettings) ?? new EditorSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new EditorSettings();
    }

    public void Update(Action<EditorSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var next = Current.Clone();
        change(next);
        Current = next;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, Current, SettingsJsonContext.Default.EditorSettings);
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings stay in effect for this session when the file cannot be written.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(EditorSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
