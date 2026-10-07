using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A titled, optionally collapsible section used to group content in side panels.</summary>
public class SectionPanel : HeaderedContentControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<SectionPanel, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<object?> HeaderActionsProperty =
        AvaloniaProperty.Register<SectionPanel, object?>(nameof(HeaderActions));

    public static readonly StyledProperty<bool> IsCollapsibleProperty =
        AvaloniaProperty.Register<SectionPanel, bool>(nameof(IsCollapsible));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SectionPanel, bool>(nameof(IsExpanded), true, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>Gets or sets the icon shown before the header.</summary>
    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Gets or sets the content placed at the right of the header row, typically small icon buttons.</summary>
    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    /// <summary>Gets or sets whether the header toggles the content's visibility.</summary>
    public bool IsCollapsible
    {
        get => GetValue(IsCollapsibleProperty);
        set => SetValue(IsCollapsibleProperty, value);
    }

    /// <summary>Gets or sets whether the content is shown.</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsExpandedProperty || change.Property == IsCollapsibleProperty)
        {
            PseudoClasses.Set(":collapsed", IsCollapsible && !IsExpanded);
        }
    }
}
