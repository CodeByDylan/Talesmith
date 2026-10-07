using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Talesmith.UI.Controls;

/// <summary>The card chrome of a modal dialog: title, description, body and a footer for actions.</summary>
/// <remarks>Its width and height are the size it takes while the window has room, and it shrinks to fit a smaller window. A dialog with a set
/// height fills it with its body, which scrolls the parts that need it, such as a list; a dialog sized by its body scrolls the body when the
/// window is too short for it.</remarks>
[TemplatePart(CloseButtonPartName, typeof(Button))]
[PseudoClasses(":fixed-height")]
public class Dialog : HeaderedContentControl
{
    private const string CloseButtonPartName = "PART_CloseButton";

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<Dialog, string?>(nameof(Description));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<Dialog, object?>(nameof(Footer));

    public static readonly StyledProperty<bool> ShowCloseButtonProperty =
        AvaloniaProperty.Register<Dialog, bool>(nameof(ShowCloseButton), true);

    private Button? _closeButton;

    /// <summary>Gets or sets the supporting text under the title.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Gets or sets the footer content, typically right-aligned action buttons.</summary>
    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>Gets or sets whether the close button is shown in the header.</summary>
    public bool ShowCloseButton
    {
        get => GetValue(ShowCloseButtonProperty);
        set => SetValue(ShowCloseButtonProperty, value);
    }

    /// <summary>Closes this dialog through its host with <paramref name="result"/>.</summary>
    public void Close(object? result = null) => DialogHost.Find(this)?.Close(this, result);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _closeButton?.Click -= OnCloseClick;
        _closeButton = e.NameScope.Find<Button>(CloseButtonPartName);
        _closeButton?.Click += OnCloseClick;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DialogLayer.RoomProperty)
        {
            var room = change.GetNewValue<Size>();
            SetValue(MaxWidthProperty, room.Width, BindingPriority.Style);
            SetValue(MaxHeightProperty, room.Height, BindingPriority.Style);
        }
        else if (change.Property == HeightProperty)
        {
            PseudoClasses.Set(":fixed-height", !double.IsNaN(Height));
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
