using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A card for an asset browser: a thumbnail or icon, the asset name and a type badge.</summary>
/// <remarks>Inside a <c>ListBox</c> with the <c>tiles</c> class, the tile shows the item's selection.</remarks>
[PseudoClasses(":selected", ":has-thumbnail")]
public class AssetTile : TemplatedControl
{
    public static readonly StyledProperty<IImage?> ThumbnailProperty =
        AvaloniaProperty.Register<AssetTile, IImage?>(nameof(Thumbnail));

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<AssetTile, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<AssetTile, string?>(nameof(Title));

    public static readonly StyledProperty<string?> TypeLabelProperty =
        AvaloniaProperty.Register<AssetTile, string?>(nameof(TypeLabel));

    public static readonly StyledProperty<IBrush?> TypeBrushProperty =
        AvaloniaProperty.Register<AssetTile, IBrush?>(nameof(TypeBrush));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<AssetTile, bool>(nameof(IsSelected));

    public static readonly StyledProperty<double> ThumbnailSizeProperty =
        AvaloniaProperty.Register<AssetTile, double>(nameof(ThumbnailSize), 72);

    /// <summary>Gets or sets the preview image; <see cref="Icon"/> is shown when null.</summary>
    public IImage? Thumbnail
    {
        get => GetValue(ThumbnailProperty);
        set => SetValue(ThumbnailProperty, value);
    }

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the short asset type shown as a badge, such as <c>PNG</c> or <c>Scene</c>.</summary>
    public string? TypeLabel
    {
        get => GetValue(TypeLabelProperty);
        set => SetValue(TypeLabelProperty, value);
    }

    /// <summary>Gets or sets the color of the type badge and icon.</summary>
    public IBrush? TypeBrush
    {
        get => GetValue(TypeBrushProperty);
        set => SetValue(TypeBrushProperty, value);
    }

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public double ThumbnailSize
    {
        get => GetValue(ThumbnailSizeProperty);
        set => SetValue(ThumbnailSizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSelectedProperty)
            PseudoClasses.Set(":selected", IsSelected);
        else if (change.Property == ThumbnailProperty)
            PseudoClasses.Set(":has-thumbnail", Thumbnail is not null);
    }
}
