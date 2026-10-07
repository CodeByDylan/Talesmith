using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A collapsible inspector section, such as one component, with an icon, header actions and a menu.</summary>
/// <remarks><see cref="Menu"/> opens from the header's more button and on right-click of the header.</remarks>
[TemplatePart(HeaderPart, typeof(Control))]
[TemplatePart(MenuButtonPart, typeof(Button))]
[PseudoClasses(":collapsed", ":has-menu")]
public class PropertyGroup : HeaderedContentControl
{
    private const string HeaderPart = "PART_Header";
    private const string MenuButtonPart = "PART_MenuButton";

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<PropertyGroup, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<IBrush?> IconForegroundProperty =
        AvaloniaProperty.Register<PropertyGroup, IBrush?>(nameof(IconForeground));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<PropertyGroup, bool>(nameof(IsExpanded), true, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<object?> HeaderActionsProperty =
        AvaloniaProperty.Register<PropertyGroup, object?>(nameof(HeaderActions));

    public static readonly StyledProperty<FlyoutBase?> MenuProperty =
        AvaloniaProperty.Register<PropertyGroup, FlyoutBase?>(nameof(Menu));

    private Control? _header;
    private Button? _menuButton;

    public Geometry? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? IconForeground
    {
        get => GetValue(IconForegroundProperty);
        set => SetValue(IconForegroundProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Gets or sets content placed at the right of the header, such as a toggle or small icon buttons.</summary>
    public object? HeaderActions
    {
        get => GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    /// <summary>Gets or sets the menu of section commands, such as reset, copy or remove.</summary>
    public FlyoutBase? Menu
    {
        get => GetValue(MenuProperty);
        set => SetValue(MenuProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _header?.PointerReleased -= OnHeaderPointerReleased;
        _menuButton?.Click -= OnMenuClick;
        _header = e.NameScope.Find<Control>(HeaderPart);
        _menuButton = e.NameScope.Find<Button>(MenuButtonPart);
        _header?.PointerReleased += OnHeaderPointerReleased;
        _menuButton?.Click += OnMenuClick;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty)
            PseudoClasses.Set(":collapsed", !IsExpanded);
        else if (change.Property == MenuProperty)
            PseudoClasses.Set(":has-menu", Menu is not null);
    }

    private void OnHeaderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right || Menu is null || _header is null)
            return;
        if (Menu is PopupFlyoutBase popup)
            popup.ShowAt(_header, true);
        else
            Menu.ShowAt(_header);
        e.Handled = true;
    }

    private void OnMenuClick(object? sender, RoutedEventArgs e)
    {
        if (Menu is not null && _menuButton is not null)
            Menu.ShowAt(_menuButton);
    }
}
