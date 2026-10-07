using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Ecs;

namespace Talesmith.Editor.Hierarchy;

/// <summary>What a hierarchy row stands for.</summary>
public enum HierarchyRowKind
{
    /// <summary>An entity the scene stores.</summary>
    Entity,

    /// <summary>An entity of a prefab instance, which the scene stores only as the instance's link.</summary>
    PrefabMember,

    /// <summary>An entity of the play session's world.</summary>
    Live
}

/// <summary>One visible row of the hierarchy: an entity at its depth, with its icon, flags and the part of its name that matches the filter.</summary>
public sealed partial class HierarchyRow : ObservableObject
{
    public const double IndentStep = 14;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private Geometry? _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Indent))]
    private int _depth;

    [ObservableProperty]
    private bool _hasChildren;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    private bool _isActive = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed), nameof(ShowVisibility))]
    private bool _isHidden;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLock))]
    private bool _isLocked;

    /// <summary>Whether the entity is the root of a prefab instance.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrefab))]
    private bool _isPrefabInstance;

    /// <summary>Whether the prefab instance differs from its prefab.</summary>
    [ObservableProperty]
    private bool _hasOverrides;

    /// <summary>Whether the prefab of an instance is missing.</summary>
    [ObservableProperty]
    private bool _isPrefabMissing;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatch))]
    private string _matchBefore = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMatch))]
    private string _matchText = "";

    [ObservableProperty]
    private string _matchAfter = "";

    /// <summary>Whether the row matches the filter only through a descendant, so it is shown dimmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDimmed))]
    private bool _isContext;

    public HierarchyRow(Guid id, HierarchyRowKind kind)
    {
        Id = id;
        Kind = kind;
    }

    /// <summary>The entity's document id; for live rows, the id the scene gave it or a stand-in for spawned entities.</summary>
    public Guid Id { get; }

    public HierarchyRowKind Kind { get; }

    /// <summary>For live rows, the play world's entity.</summary>
    public Entity LiveEntity { get; init; }

    public Thickness Indent => new(Depth * IndentStep, 0, 0, 0);

    /// <summary>Whether the row shows a filter match, split around the matched text.</summary>
    public bool HasMatch => MatchBefore.Length > 0 || MatchText.Length > 0;

    public bool IsPrefab => IsPrefabInstance || Kind == HierarchyRowKind.PrefabMember;

    public bool IsMember => Kind == HierarchyRowKind.PrefabMember;

    public bool IsEditable => Kind == HierarchyRowKind.Entity;

    public bool IsDimmed => !IsActive || IsHidden || IsContext;

    /// <summary>Whether the visibility toggle shows without hovering, because the entity is hidden.</summary>
    public bool ShowVisibility => IsHidden;

    public bool ShowLock => IsLocked;

    /// <summary>Sets the filter match: the matched range of the name, or none.</summary>
    public void SetMatch(int start, int length)
    {
        if (length <= 0 || start < 0 || start + length > Name.Length)
        {
            MatchBefore = "";
            MatchText = "";
            MatchAfter = "";
            return;
        }

        MatchBefore = Name[..start];
        MatchText = Name.Substring(start, length);
        MatchAfter = Name[(start + length)..];
    }
}
