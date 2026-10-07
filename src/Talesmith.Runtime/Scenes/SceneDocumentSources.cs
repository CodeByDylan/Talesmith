using System.Collections.Concurrent;
using Talesmith.Assets;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Scenes;

/// <summary>Supplies scene documents for the "scene" scene before it loads them from asset files, such as unsaved scenes in the editor.</summary>
/// <remarks>Register sources with dependency injection; they are asked in reverse registration order and the first document wins.</remarks>
public interface ISceneDocumentSource
{
    /// <summary>Gets the document for a request, or null to let the next source or the asset files answer.</summary>
    ValueTask<SceneDocument?> TryGetAsync(SceneRequest request, CancellationToken cancellationToken);
}

/// <summary>Scene documents held in memory: hosts run a document without writing it to a file, or replace a scene file's content.</summary>
public sealed class InMemorySceneDocuments(IAssetCatalog catalog) : ISceneDocumentSource
{
    /// <summary>The request parameter naming a document added with <see cref="Add"/>.</summary>
    public const string DocumentParameter = "document";

    private readonly ConcurrentDictionary<string, SceneDocument> _documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SceneDocument> _overrides = new(StringComparer.Ordinal);

    /// <summary>Keeps a document and returns the request that loads it; the document is used as it is, so pass a copy if it will change.</summary>
    public SceneRequest Add(SceneDocument document, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        key ??= document.Id == Guid.Empty ? Guid.NewGuid().ToString("N") : document.Id.ToString("N");
        _documents[key] = document;
        return new SceneRequest(DocumentScene.SceneName, new Dictionary<string, string> { [DocumentParameter] = key });
    }

    /// <summary>Uses a document instead of the scene file at <paramref name="path"/>, whether the scene is requested by path or guid.</summary>
    public void Override(string path, SceneDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _overrides[AssetPath.Normalize(path)] = document;
    }

    public bool Remove(string keyOrPath) => _documents.TryRemove(keyOrPath, out _) | _overrides.TryRemove(AssetPath.Normalize(keyOrPath), out _);

    public void Clear()
    {
        _documents.Clear();
        _overrides.Clear();
    }

    public ValueTask<SceneDocument?> TryGetAsync(SceneRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Get(DocumentParameter) is { } key && _documents.TryGetValue(key, out var document))
            return ValueTask.FromResult<SceneDocument?>(document);
        if (_overrides.IsEmpty)
            return ValueTask.FromResult<SceneDocument?>(null);

        var path = request.Get(DocumentScene.PathParameter);
        if (path is null && AssetGuid.TryParse(request.Get(DocumentScene.GuidParameter), null, out var guid) && catalog.TryGetPath(guid, out var guidPath))
            path = guidPath;
        return ValueTask.FromResult(path is not null && _overrides.TryGetValue(AssetPath.Normalize(path), out document) ? document : null);
    }
}
