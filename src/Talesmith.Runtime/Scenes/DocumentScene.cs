using Talesmith.Assets;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Scenes;

/// <summary>A built-in scene that runs a scene document, registered as "scene".</summary>
/// <remarks>
/// Parameters: <c>path</c>, the <c>.tscene</c> asset path, or <c>guid</c>, its asset guid. Registered <see cref="ISceneDocumentSource"/>s,
/// such as <see cref="InMemorySceneDocuments"/>, are asked for the document first. The document's environment sets the clear color and
/// <see cref="Scene.Environment"/>. After its entities are created, <see cref="MapLoaded"/> is raised for each tile map, as the "map"
/// scene does, so gameplay plugins work with either.
/// </remarks>
public sealed class DocumentScene(SceneInstantiator instantiator, InMemorySceneDocuments memory, IEnumerable<ISceneDocumentSource> sources,
    IAssetManager assets, IAssetCatalog catalog, AssetReferences references, IEventBus events) : Scene
{
    public const string SceneName = "scene";
    public const string PathParameter = "path";
    public const string GuidParameter = "guid";

    /// <summary>The document the scene was created from; it may be shared with the asset cache, so copy it before changing it.</summary>
    public SceneDocument? Document { get; private set; }

    /// <summary>The created entities by document id.</summary>
    public InstantiationResult? Entities { get; private set; }

    /// <summary>Creates a request that runs the scene file at <paramref name="path"/>.</summary>
    public static SceneRequest ForPath(string path) => new(SceneName, new Dictionary<string, string> { [PathParameter] = path });

    /// <summary>Creates a request that runs the scene asset with <paramref name="guid"/>.</summary>
    public static SceneRequest ForGuid(AssetGuid guid) => new(SceneName, new Dictionary<string, string> { [GuidParameter] = guid.ToString() });

    protected internal override async Task LoadAsync(CancellationToken cancellationToken)
    {
        var document = await FindAsync(cancellationToken);
        Document = document;
        Environment = document.Environment;
        ClearColor = document.Environment.ClearColor;
        Entities = await instantiator.InstantiateAsync(World, document, LoadProgress, cancellationToken);

        var maps = new List<(Ecs.Entity Entity, TileMapComponent Map)>();
        foreach (var archetype in World.Query<TileMapComponent>())
        {
            var components = archetype.GetSpan<TileMapComponent>();
            for (var i = 0; i < components.Length; i++)
                maps.Add((archetype.Entities[i], components[i]));
        }

        foreach (var (entity, map) in maps)
            events.Publish(new MapLoaded(entity, map.Map, World));
    }

    private async Task<SceneDocument> FindAsync(CancellationToken cancellationToken)
    {
        if (await memory.TryGetAsync(Request, cancellationToken) is { } fromMemory)
            return fromMemory;
        foreach (var source in sources.Reverse())
        {
            if (!ReferenceEquals(source, memory) && await source.TryGetAsync(Request, cancellationToken) is { } supplied)
                return supplied;
        }

        var path = Request.Get(PathParameter);
        var guid = AssetGuid.Empty;
        if (path is null)
        {
            var text = Request.Get(GuidParameter) ?? throw new InvalidOperationException($"The scene '{SceneName}' needs a '{PathParameter}' or '{GuidParameter}' parameter.");
            if (!AssetGuid.TryParse(text, null, out guid) || !catalog.TryGetPath(guid, out path))
                throw new AssetException($"The scene asset {text} is not in the asset catalog.");
        }

        var document = await assets.LoadAsync<SceneDocument>(path, cancellationToken);
        if (!guid.IsEmpty || catalog.TryGetGuid(path, out guid))
            references.Register(document, guid);
        return document;
    }
}
