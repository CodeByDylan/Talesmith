using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Assets;

namespace Talesmith.Editor.Assets.Browser;

/// <summary>A folder in the Assets panel's folder tree, or the Favorites entry above it.</summary>
public sealed partial class FolderNodeViewModel(string path, string name, Geometry icon, bool isFavorites = false) : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>The folder's asset path; "" for the asset root.</summary>
    public string Path { get; } = path;

    public string Name { get; } = name;

    public Geometry Icon { get; } = icon;

    /// <summary>Whether this is the Favorites entry, which lists favorite assets rather than a folder.</summary>
    public bool IsFavorites { get; } = isFavorites;

    public bool IsRoot => Path.Length == 0 && !IsFavorites;

    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    /// <summary>Finds the node of a folder in this subtree.</summary>
    public FolderNodeViewModel? Find(string path)
    {
        if (!IsFavorites && AssetPath.Comparer.Equals(Path, path))
            return this;
        foreach (var child in Children)
        {
            if (child.Path.Length == 0 || AssetPath.Comparer.Equals(child.Path, path) || AssetPath.IsWithin(path, child.Path))
            {
                if (child.Find(path) is { } found)
                    return found;
            }
        }

        return null;
    }
}

/// <summary>A folder in the breadcrumb bar.</summary>
public sealed record BreadcrumbViewModel(string Name, string Path, bool IsLast);

/// <summary>A kind filter chip with the number of matching assets.</summary>
public sealed partial class KindFilterViewModel(AssetKind kind) : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private int _count;

    public AssetKind Kind { get; } = kind;

    public string Title => Kind.DisplayName;

    public Geometry Icon => AssetKindStyle.Of(Kind).Icon;

    public IBrush Brush => AssetKindStyle.Of(Kind).Brush;
}
