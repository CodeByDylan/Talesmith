using System.Globalization;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Events;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Runtime.Maps;

/// <summary>A built-in scene that shows one or more tile maps, registered as "map".</summary>
/// <remarks>
/// Parameters: <c>map</c>, the map's asset path (required), or several paths separated by semicolons; <c>focus</c>, the name of a map
/// object to center the camera on; <c>zoom</c>, the starting zoom. Maps share the world origin and are stacked in the order listed, the
/// first at the back, so a hexagonal map and a rectangular map can be layered. The camera is limited to the bounds of all maps.
/// Gameplay plugins react to <see cref="MapLoaded"/>, raised once per map, to add players and behaviour.
/// </remarks>
public sealed class MapScene(IAssetManager assets, MapSpawner spawner, IEventBus events) : Scene
{
    public const string SceneName = "map";

    /// <summary>The loaded maps, back to front.</summary>
    public IReadOnlyList<TileMap> Maps { get; private set; } = [];

    protected internal override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var paths = Request.Require("map").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length == 0)
            throw new InvalidOperationException($"The scene '{SceneName}' needs at least one map in its 'map' parameter.");

        var maps = new TileMap[paths.Length];
        LoadProgress.Expect(paths.Length);
        for (var i = 0; i < paths.Length; i++)
        {
            maps[i] = await assets.LoadAsync<TileMap>(paths[i], cancellationToken);
            LoadProgress.Advance();
        }

        Maps = maps;
        ClearColor = maps.Select(m => m.BackgroundColor).FirstOrDefault(c => c is not null);

        var mapEntities = new Ecs.Entity[maps.Length];
        var renderLayer = RenderLayers.Terrain;
        var bounds = Rect2.Empty;
        for (var i = 0; i < maps.Length; i++)
        {
            mapEntities[i] = spawner.Spawn(World, maps[i], renderLayer: renderLayer);
            renderLayer += maps[i].Layers.Count;
            bounds = bounds.Union(maps[i].MeasureContentBounds());
        }

        var focus = bounds.Center;
        if (Request.Get("focus") is { } focusName && maps.SelectMany(m => m.ObjectLayers).SelectMany(l => l.Objects).FirstOrDefault(o => o.Name == focusName) is { } target)
            focus = target.Position;
        var zoom = float.TryParse(Request.Get("zoom"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
        World.Create(new Transform(focus), new Camera(focus, zoom) { Bounds = bounds.IsEmpty ? null : bounds }, new Name("Main camera"));

        for (var i = 0; i < maps.Length; i++)
            events.Publish(new MapLoaded(mapEntities[i], maps[i], World));
    }
}
