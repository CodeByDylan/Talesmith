using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Assets;
using Talesmith.Authoring;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Runtime.Scenes;

/// <summary>Registers scene documents, prefabs and the built-in authorable components.</summary>
public static class SceneDocumentServiceCollectionExtensions
{
    /// <summary>
    /// Adds the document serializer and its importers for <c>.tscene</c> and <c>.tprefab</c> files, the value converters and component
    /// registry, the instantiator, capture and prefab service, in-memory scene documents and the built-in components.
    /// </summary>
    /// <remarks>
    /// Needs the asset manager, the texture cache and the map spawner, which <c>GameBuilder</c> registers. Registers an empty
    /// <see cref="IAssetCatalog"/> when none is registered yet, so scenes still load by path.
    /// </remarks>
    public static IServiceCollection AddTalesmithScenes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IAssetCatalog, EmptyAssetCatalog>();
        services.TryAddSingleton(sp => new DocumentSerializer(sp.GetServices<IDocumentMigration>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, SceneDocumentImporter>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAssetImporter, PrefabDocumentImporter>());

        services.TryAddSingleton<ValueConverterRegistry>();
        services.AddValueConverter<TextureConverter>();
        services.TryAddSingleton<ComponentRegistry>();
        services.TryAddSingleton<AssetReferences>();
        services.TryAddSingleton<SceneInstantiator>();
        services.TryAddSingleton<SceneCapture>();
        services.TryAddSingleton<IPrefabService, PrefabService>();
        services.TryAddSingleton<InMemorySceneDocuments>();

        services.AddComponent<Transform>();
        services.AddComponent<Sprite>();
        services.AddComponent<SpriteAnimator>();
        services.AddComponent<Camera>();
        services.AddComponent<TileMapRenderer>();
        services.AddComponent<Tags>();
        services.AddComponent<Parent>();
        services.AddComponent<LocalTransform>();
        services.AddComponent<Inactive>();
        services.AddComponent<PrefabInstance>();
        services.AddComponent<SceneEntityId>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IComponentDefinition, TransformDefinition>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IComponentDefinition, SpriteDefinition>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IComponentDefinition, CameraDefinition>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IComponentDefinition, TileMapRendererDefinition>());

        services.AddScene<DocumentScene>(DocumentScene.SceneName);
        return services;
    }

    private sealed class EmptyAssetCatalog : IAssetCatalog
    {
        public bool TryGetPath(AssetGuid guid, [NotNullWhen(true)] out string? path)
        {
            path = null;
            return false;
        }

        public bool TryGetGuid(string path, out AssetGuid guid)
        {
            guid = default;
            return false;
        }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}

/// <summary>Loads <c>.tscene</c> files as <see cref="SceneDocument"/>s.</summary>
internal sealed class SceneDocumentImporter(DocumentSerializer serializer) : AssetImporter<SceneDocument>
{
    public override IReadOnlyList<string> Extensions { get; } = [".tscene"];

    public override async Task<SceneDocument> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        await using var stream = context.OpenRead();
        return serializer.ReadScene(stream);
    }
}

/// <summary>Loads <c>.tprefab</c> files as <see cref="PrefabDocument"/>s.</summary>
internal sealed class PrefabDocumentImporter(DocumentSerializer serializer) : AssetImporter<PrefabDocument>
{
    public override IReadOnlyList<string> Extensions { get; } = [".tprefab"];

    public override async Task<PrefabDocument> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        await using var stream = context.OpenRead();
        return serializer.ReadPrefab(stream);
    }
}
