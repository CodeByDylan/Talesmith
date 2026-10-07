using System.Numerics;
using Talesmith.Editor.Documents;
using Talesmith.Editor.DragAndDrop;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;

namespace Talesmith.Editor.Hierarchy;

/// <summary>Creates the entities that dropped assets make, for the hierarchy and the scene viewport: prefab instances for <c>.tprefab</c> files,
/// and entities named after the asset for textures, tile maps, particle presets and sounds.</summary>
public sealed class SceneAssetDrop(ISceneDocumentService documents, ISelectionService selection, IUndoService undo, EntityTemplates templates, PrefabWorkflow prefabs)
{
    /// <summary>Whether an asset makes an entity.</summary>
    public bool CanCreate(DraggedAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return PrefabDocuments.IsPrefabPath(asset.Path) || templates.FromAsset(asset) is not null;
    }

    /// <summary>Creates the entity of one asset under <paramref name="parent"/>, at a local position; returns its id, or null.</summary>
    public Guid? Create(DraggedAsset asset, Guid? parent, int siblingIndex, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (documents.Active is not { } model)
            return null;
        if (PrefabDocuments.IsPrefabPath(asset.Path))
            return prefabs.Instantiate(asset.Guid, parent, position, siblingIndex);
        if (templates.FromAsset(asset) is not { } components)
            return null;
        var name = UniqueName(model, Path.GetFileNameWithoutExtension(asset.Path), parent);
        return model.CreateEntity(name, parent, [EntityTemplates.Transform(position), .. components], siblingIndex).Id;
    }

    /// <summary>Creates the entities of several assets as one undo step and selects them; each next one goes after the previous.</summary>
    public IReadOnlyList<Guid> Drop(IReadOnlyList<DraggedAsset> assets, Guid? parent, int siblingIndex, Vector2 position)
    {
        ArgumentNullException.ThrowIfNull(assets);
        var created = new List<Guid>();
        if (documents.Active is null || assets.Count == 0)
            return created;
        using (undo.BeginTransaction(assets.Count == 1 ? $"Add {Path.GetFileNameWithoutExtension(assets[0].Path)}" : $"Add {assets.Count} assets"))
        {
            foreach (var asset in assets)
            {
                if (Create(asset, parent, siblingIndex, position) is not { } id)
                    continue;
                created.Add(id);
                if (siblingIndex >= 0)
                    siblingIndex++;
            }
        }

        if (created.Count > 0)
            selection.SelectEntities(created);
        return created;
    }

    /// <summary>The name, or the name with the lowest free number, so siblings stay apart.</summary>
    public static string UniqueName(SceneDocumentModel model, string name, Guid? parent)
    {
        ArgumentNullException.ThrowIfNull(model);
        var names = model.GetChildren(parent).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!names.Contains(name))
            return name;
        for (var i = 1; ; i++)
        {
            if (!names.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }
}
