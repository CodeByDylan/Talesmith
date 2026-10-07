using Talesmith.Assets;

namespace Talesmith.Editor.Selection;

/// <summary>The default <see cref="ISelectionService"/>.</summary>
public sealed class SelectionService : ISelectionService
{
    private readonly OrderedSet<Guid> _entities = new();
    private readonly OrderedSet<AssetGuid> _assets = new();
    private readonly OrderedSet<object> _objects = new();

    public IReadOnlyList<Guid> Entities => _entities.Items;

    public IReadOnlyList<AssetGuid> Assets => _assets.Items;

    public IReadOnlyList<object> Objects => _objects.Items;

    public object? Primary { get; private set; }

    public bool IsEmpty => _entities.Count == 0 && _assets.Count == 0 && _objects.Count == 0;

    public event EventHandler<SelectionChangedEventArgs>? Changed;

    public void SelectEntities(IEnumerable<Guid> entities, SelectionMode mode = SelectionMode.Replace) =>
        Select(_entities, entities, mode, SelectionKinds.Entities);

    public void SelectAssets(IEnumerable<AssetGuid> assets, SelectionMode mode = SelectionMode.Replace) =>
        Select(_assets, assets, mode, SelectionKinds.Assets);

    public void SelectObjects(IEnumerable<object> objects, SelectionMode mode = SelectionMode.Replace) =>
        Select(_objects, objects, mode, SelectionKinds.Objects);

    public bool IsSelected(Guid entity) => _entities.Contains(entity);

    public bool IsSelected(AssetGuid asset) => _assets.Contains(asset);

    public bool IsSelected(object item) => item switch
    {
        Guid entity => _entities.Contains(entity),
        AssetGuid asset => _assets.Contains(asset),
        _ => _objects.Contains(item)
    };

    public void Clear()
    {
        var kinds = KindsPresent();
        if (kinds == SelectionKinds.None)
            return;
        _entities.Clear();
        _assets.Clear();
        _objects.Clear();
        Primary = null;
        Changed?.Invoke(this, new SelectionChangedEventArgs(kinds));
    }

    private void Select<T>(OrderedSet<T> set, IEnumerable<T> items, SelectionMode mode, SelectionKinds kind)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items as IReadOnlyCollection<T> ?? [.. items];
        var changed = SelectionKinds.None;
        if (mode == SelectionMode.Replace)
        {
            var others = KindsPresent() & ~kind;
            if (others != SelectionKinds.None)
            {
                if (kind != SelectionKinds.Entities)
                    _entities.Clear();
                if (kind != SelectionKinds.Assets)
                    _assets.Clear();
                if (kind != SelectionKinds.Objects)
                    _objects.Clear();
                changed |= others;
            }

            if (!set.SequenceEqual(list))
            {
                set.Clear();
                foreach (var item in list)
                    set.Add(item);
                changed |= kind;
            }
        }
        else
        {
            foreach (var item in list)
            {
                var modified = mode switch
                {
                    SelectionMode.Add => set.Add(item),
                    SelectionMode.Remove => set.Remove(item),
                    _ => set.Contains(item) ? set.Remove(item) : set.Add(item)
                };
                if (modified)
                    changed |= kind;
            }
        }

        if (changed == SelectionKinds.None)
            return;
        Primary = set.Count > 0 ? set.Items[^1] : _entities.Count > 0 ? _entities.Items[^1] : _assets.Count > 0 ? _assets.Items[^1] : _objects.Count > 0 ? _objects.Items[^1] : null;
        Changed?.Invoke(this, new SelectionChangedEventArgs(changed));
    }

    private SelectionKinds KindsPresent() =>
        (_entities.Count > 0 ? SelectionKinds.Entities : 0) | (_assets.Count > 0 ? SelectionKinds.Assets : 0) | (_objects.Count > 0 ? SelectionKinds.Objects : 0);

    private sealed class OrderedSet<T>
        where T : notnull
    {
        private readonly List<T> _items = [];
        private readonly HashSet<T> _set = [];

        public IReadOnlyList<T> Items => _items;

        public int Count => _items.Count;

        public bool Contains(T item) => _set.Contains(item);

        public bool Add(T item)
        {
            if (!_set.Add(item))
                return false;
            _items.Add(item);
            return true;
        }

        public bool Remove(T item)
        {
            if (!_set.Remove(item))
                return false;
            _items.Remove(item);
            return true;
        }

        public void Clear()
        {
            _items.Clear();
            _set.Clear();
        }

        public bool SequenceEqual(IReadOnlyCollection<T> other) => _items.SequenceEqual(other.Distinct());
    }
}
