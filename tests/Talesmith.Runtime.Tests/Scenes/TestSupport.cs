using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Events;
using Talesmith.Grids;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Tests.Scenes;

/// <summary>An asset catalog filled by tests.</summary>
internal sealed class FakeCatalog : IAssetCatalog
{
    private readonly Dictionary<AssetGuid, string> _paths = new();

    public event EventHandler? Changed;

    public AssetGuid Add(string path, AssetGuid? guid = null)
    {
        var id = guid ?? AssetGuid.NewGuid();
        _paths[id] = path;
        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public bool TryGetPath(AssetGuid guid, [NotNullWhen(true)] out string? path) => _paths.TryGetValue(guid, out path);

    public bool TryGetGuid(string path, out AssetGuid guid)
    {
        foreach (var (key, value) in _paths)
        {
            if (value == path)
            {
                guid = key;
                return true;
            }
        }

        guid = default;
        return false;
    }
}

/// <summary>Asset files held in memory.</summary>
internal sealed class MemoryAssetSource : IAssetSource
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public string Description => "memory";

    public void Write(string path, string text) => _files[AssetPath.Normalize(path)] = Encoding.UTF8.GetBytes(text);

    public bool Exists(string path) => _files.ContainsKey(AssetPath.Normalize(path));

    public Stream OpenRead(string path) =>
        _files.TryGetValue(AssetPath.Normalize(path), out var bytes) ? new MemoryStream(bytes, false) : throw new FileNotFoundException(path);

    public IEnumerable<string> List(string folder, string pattern = "*", bool recursive = false) => _files.Keys;

    public string? GetFullPath(string path) => null;
}

/// <summary>Imports any ".sheet" file as a 64×32 texture with sprites "left" and "right" and a looping "walk" animation.</summary>
internal sealed class SheetImporter : AssetImporter<TextureAsset>
{
    public override IReadOnlyList<string> Extensions { get; } = [".sheet"];

    public override Task<TextureAsset> ImportAsync(AssetImportContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new TextureAsset(context.Path, new ImageData(64, 32, new byte[64 * 32 * 4]))
        {
            Sprites =
            [
                new SpriteSlice("left", new Rect2(0, 0, 32, 32), new Vector2(0.5f, 1)),
                new SpriteSlice("right", new Rect2(32, 0, 32, 32), new Vector2(0.25f, 0.75f))
            ],
            Animations = [new SpriteAnimationInfo("walk", ["left", "right"], 10)]
        });
}

/// <summary>Imports any ".testmap" file as a small square map with one object, "Spawn", at cell (2, 3).</summary>
internal sealed class TestMapImporter : AssetImporter<TileMap>
{
    public override IReadOnlyList<string> Extensions { get; } = [".testmap"];

    public override Task<TileMap> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        var layout = new SquareLayout(64, 64);
        var cell = new GridCoord(2, 3);
        var point = new MapObject(1, "Spawn", "spawn", MapObjectShape.Point, layout.CellToWorld(cell), cell, [], TileCell.Empty, PropertySet.Empty);
        var layers = new MapLayer[] { new TileLayer("Ground", 5), new ObjectLayer("Objects", [point]) };
        return Task.FromResult(new TileMap(context.Path, layout, 5, [], layers, PropertySet.Empty));
    }
}

/// <summary>Collects log messages so tests can check what was reported.</summary>
internal sealed class ListLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new ListLogger(this);

    public void Dispose()
    {
    }

    public bool Contains(LogLevel level, string text) => Entries.Any(e => e.Level == level && e.Message.Contains(text, StringComparison.Ordinal));

    private sealed class ListLogger(ListLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}

/// <summary>The scene services over in-memory assets, without a game loop.</summary>
internal sealed class SceneTestHost : IDisposable
{
    public SceneTestHost(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(Logs));
        services.AddSingleton<IRenderer, NullRenderer>();
        services.AddSingleton(new GameSettings());
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<TextureCache>();
        services.AddSingleton<MapSpawner>();
        services.AddSingleton<IAssetSource>(Files);
        services.AddSingleton<IAssetCatalog>(Catalog);
        services.AddSingleton<IAssetManager, AssetManager>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, SheetImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, TestMapImporter>());
        services.AddTalesmithScenes();
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public ListLoggerProvider Logs { get; } = new();

    public MemoryAssetSource Files { get; } = new();

    public FakeCatalog Catalog { get; } = new();

    public ServiceProvider Services { get; }

    public SceneInstantiator Instantiator => Services.GetRequiredService<SceneInstantiator>();

    public SceneCapture Capture => Services.GetRequiredService<SceneCapture>();

    public ComponentRegistry Components => Services.GetRequiredService<ComponentRegistry>();

    public AssetGuid AddFile(string path, string content = "")
    {
        Files.Write(path, content);
        return Catalog.Add(path);
    }

    public AssetGuid AddPrefab(string path, PrefabDocument prefab) => AddFile(path, DocumentSerializer.Write(prefab));

    public void Dispose() => Services.Dispose();
}
