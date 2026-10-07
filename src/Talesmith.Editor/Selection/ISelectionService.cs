using Talesmith.Assets;

namespace Talesmith.Editor.Selection;

/// <summary>How a selection request combines with the current selection.</summary>
public enum SelectionMode
{
    /// <summary>Selects only the given items, clearing every kind of selection.</summary>
    Replace,

    Add,

    /// <summary>Adds unselected items and removes selected ones, as Ctrl+click does.</summary>
    Toggle,

    Remove
}

/// <summary>What the editor is working on: scene entities by document id, assets by guid, and other objects tools select, such as tiles.</summary>
/// <remarks>
/// A selection holds one kind at a time when <see cref="SelectionMode.Replace"/> is used, which is what clicking does; adding keeps other
/// kinds. Entity ids are the <c>EntityDocument.Id</c>s of the open scene. The last item selected is <see cref="Primary"/>, which the inspector
/// shows. All members are used on the UI thread.
/// </remarks>
public interface ISelectionService
{
    /// <summary>The selected entities in selection order.</summary>
    IReadOnlyList<Guid> Entities { get; }

    /// <summary>The selected assets in selection order.</summary>
    IReadOnlyList<AssetGuid> Assets { get; }

    /// <summary>Other selected objects in selection order.</summary>
    IReadOnlyList<object> Objects { get; }

    /// <summary>The last selected item: a <see cref="Guid"/> entity id, an <see cref="AssetGuid"/> or an object; null when nothing is selected.</summary>
    object? Primary { get; }

    bool IsEmpty { get; }

    event EventHandler<SelectionChangedEventArgs>? Changed;

    void SelectEntities(IEnumerable<Guid> entities, SelectionMode mode = SelectionMode.Replace);

    void SelectAssets(IEnumerable<AssetGuid> assets, SelectionMode mode = SelectionMode.Replace);

    void SelectObjects(IEnumerable<object> objects, SelectionMode mode = SelectionMode.Replace);

    bool IsSelected(Guid entity);

    bool IsSelected(AssetGuid asset);

    bool IsSelected(object item);

    void Clear();
}

/// <summary>Which kinds of selection changed.</summary>
[Flags]
public enum SelectionKinds
{
    None = 0,
    Entities = 1,
    Assets = 2,
    Objects = 4
}

public sealed class SelectionChangedEventArgs(SelectionKinds kinds) : EventArgs
{
    public SelectionKinds Kinds { get; } = kinds;
}

public static class SelectionServiceExtensions
{
    public static void SelectEntity(this ISelectionService selection, Guid entity, SelectionMode mode = SelectionMode.Replace) =>
        selection.SelectEntities([entity], mode);

    public static void SelectAsset(this ISelectionService selection, AssetGuid asset, SelectionMode mode = SelectionMode.Replace) =>
        selection.SelectAssets([asset], mode);
}
