using Talesmith.Assets;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Prefabs;

/// <summary>Lets a prefab be edited as a scene: converts between prefab and scene documents and tells prefab paths apart.</summary>
public static class PrefabDocuments
{
    public const string Extension = ".tprefab";

    public static bool IsPrefabPath(string? assetPath) =>
        assetPath is not null && AssetPath.GetExtension(assetPath).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>A scene with the prefab's entities, for editing in the viewport.</summary>
    public static SceneDocument ToScene(PrefabDocument prefab)
    {
        ArgumentNullException.ThrowIfNull(prefab);
        return new SceneDocument { Id = prefab.Id == Guid.Empty ? Guid.NewGuid() : prefab.Id, Entities = prefab.Entities.ConvertAll(e => e.Clone()) };
    }

    /// <summary>The prefab a scene edited as a prefab saves as.</summary>
    public static PrefabDocument ToPrefab(SceneDocument scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new PrefabDocument { Id = scene.Id, Entities = scene.Entities.ConvertAll(e => e.Clone()) };
    }
}
