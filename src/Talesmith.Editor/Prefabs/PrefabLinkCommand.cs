using Talesmith.Editor.Documents;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Prefabs;

/// <summary>Changes the prefab link of an instance, such as its overrides; edits with the same <paramref name="mergeKey"/> merge, as dragging a value does.</summary>
internal sealed class PrefabLinkCommand(SceneDocumentModel model, string description, Guid instance, PrefabLink oldLink, PrefabLink newLink, string? mergeKey)
    : IUndoableCommand
{
    private PrefabLink _new = newLink;

    public string Description { get; } = description;

    public object? Document => model;

    public void Apply() => Set(_new);

    public void Revert() => Set(oldLink);

    public bool TryMerge(IUndoableCommand next)
    {
        if (mergeKey is null || next is not PrefabLinkCommand other || other.Key != mergeKey || other.Instance != instance || !ReferenceEquals(other.Document, model))
            return false;
        _new = other._new;
        return true;
    }

    private string? Key => mergeKey;

    private Guid Instance => instance;

    private void Set(PrefabLink link)
    {
        if (model.Find(instance) is not { } current)
            return;
        var entity = current.Clone();
        entity.Prefab = link.Clone();
        model.RawReplace(entity);
    }
}
