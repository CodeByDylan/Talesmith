namespace Talesmith.UI.Docking;

/// <summary>How a <see cref="DockSplit"/> lays out its children.</summary>
public enum DockOrientation
{
    /// <summary>Children side by side, from left to right.</summary>
    Horizontal,

    /// <summary>Children stacked from top to bottom.</summary>
    Vertical
}

/// <summary>A side of a node where a panel can be docked.</summary>
public enum DockEdge
{
    Left,
    Top,
    Right,
    Bottom
}

/// <summary>A node of a <see cref="DockLayout"/>: a split or a group of tabbed panels.</summary>
public abstract class DockNode
{
    private double _size = 1;

    private protected DockNode(string? id, string prefix)
    {
        Id = string.IsNullOrEmpty(id) ? NewId(prefix) : id;
    }

    /// <summary>Gets the identifier, unique within a layout.</summary>
    public string Id { get; }

    /// <summary>Gets the share of the parent split this node takes, relative to its siblings.</summary>
    public double Size
    {
        get => _size;
        init => _size = value;
    }

    /// <summary>Gets whether the node is hidden, letting its siblings take its space.</summary>
    public bool IsCollapsed { get; internal set; }

    /// <summary>Gets the split containing this node, or null for the root.</summary>
    public DockSplit? Parent { get; internal set; }

    internal void SetSize(double size) => _size = double.IsFinite(size) && size > 0 ? size : 1e-3;

    internal static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..(prefix.Length + 9)];
}

/// <summary>A node that divides its space between child nodes along one axis.</summary>
public sealed class DockSplit : DockNode
{
    private readonly List<DockNode> _children = [];

    public DockSplit(DockOrientation orientation, params IEnumerable<DockNode> children)
        : this(null, orientation, children)
    {
    }

    public DockSplit(string? id, DockOrientation orientation, params IEnumerable<DockNode> children)
        : base(id, "split")
    {
        Orientation = orientation;
        foreach (var child in children)
            Insert(_children.Count, child);
    }

    public DockOrientation Orientation { get; }

    public IReadOnlyList<DockNode> Children => _children;

    internal void Insert(int index, DockNode child)
    {
        child.Parent = this;
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
    }

    internal void Remove(DockNode child)
    {
        if (_children.Remove(child))
            child.Parent = null;
    }

    internal void Replace(DockNode child, DockNode replacement)
    {
        var index = _children.IndexOf(child);
        if (index < 0)
            return;
        child.Parent = null;
        replacement.Parent = this;
        _children[index] = replacement;
    }
}

/// <summary>A node showing one or more panels as tabs, one of them active.</summary>
public sealed class DockGroup : DockNode
{
    private readonly List<string> _panels = [];

    /// <summary>Creates a group; a null <paramref name="id"/> gets a generated one.</summary>
    public DockGroup(string? id, params IEnumerable<string> panels)
        : base(id, "group")
    {
        foreach (var panel in panels)
        {
            if (!_panels.Contains(panel))
                _panels.Add(panel);
        }

        ActivePanel = _panels.FirstOrDefault();
    }

    /// <summary>Gets the panel ids in tab order.</summary>
    public IReadOnlyList<string> Panels => _panels;

    /// <summary>Gets the id of the panel whose content is shown.</summary>
    public string? ActivePanel { get; internal set; }

    internal void Insert(int index, string panel)
    {
        _panels.Remove(panel);
        _panels.Insert(index < 0 ? _panels.Count : Math.Clamp(index, 0, _panels.Count), panel);
    }

    internal int Remove(string panel)
    {
        var index = _panels.IndexOf(panel);
        if (index < 0)
            return -1;
        _panels.RemoveAt(index);
        if (ActivePanel == panel)
            ActivePanel = _panels.Count == 0 ? null : _panels[Math.Min(index, _panels.Count - 1)];
        return index;
    }
}
