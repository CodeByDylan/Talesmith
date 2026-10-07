using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Talesmith.Assets;

namespace Talesmith.Editor.Projects;

/// <summary>Editor state of the open project that is not part of the game, such as the last scene and viewport cameras, kept in
/// <c>.talesmith/editor.json</c>.</summary>
/// <remarks>Values are JSON-serialized per key; changes are written shortly after the last change and when the project closes.</remarks>
public sealed class ProjectState : IAsyncDisposable
{
    public const string FileName = "editor.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly JsonObject _values;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    public ProjectState(EditorProject project)
    {
        _path = project.GetStatePath(FileName);
        _values = Load(_path);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _saveTimer.Tick += (_, _) => _ = SaveAsync();
    }

    /// <summary>Gets the value stored under <paramref name="key"/>, or null when there is none or it does not fit <typeparamref name="T"/>.</summary>
    public T? Get<T>(string key)
    {
        if (_values[key] is not { } node)
            return default;
        try
        {
            return node.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Stores a value; null removes it.</summary>
    public void Set<T>(string key, T? value)
    {
        _values[key] = value is null ? null : JsonSerializer.SerializeToNode(value, Options);
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public async Task SaveAsync()
    {
        _saveTimer.Stop();
        if (!_dirty)
            return;
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await AtomicFile.WriteAllTextAsync(_path, _values.ToJsonString(Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Editor state is a convenience; the project works without it.
        }
    }

    public async ValueTask DisposeAsync() => await SaveAsync();

    private static JsonObject Load(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject values ? values : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
