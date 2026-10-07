using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace Talesmith.UI.Controls;

/// <summary>A single notification card displayed by <see cref="ToastHost"/>.</summary>
[TemplatePart(CloseButtonPartName, typeof(Button))]
[PseudoClasses(":info", ":success", ":warning", ":error", ":shown")]
public class Toast : TemplatedControl
{
    private const string CloseButtonPartName = "PART_CloseButton";

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Toast, string?>(nameof(Title));

    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<Toast, string?>(nameof(Message));

    public static readonly StyledProperty<ToastKind> KindProperty =
        AvaloniaProperty.Register<Toast, ToastKind>(nameof(Kind));

    private Button? _closeButton;

    public Toast()
    {
        UpdateKindClasses();
    }

    /// <summary>Raised when the user dismisses the toast.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Gets or sets the headline.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the optional body text.</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Gets or sets the severity.</summary>
    public ToastKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Gets or sets whether the toast is in its visible state, which drives the enter and exit transitions.</summary>
    public bool IsShown
    {
        get => PseudoClasses.Contains(":shown");
        set => PseudoClasses.Set(":shown", value);
    }

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

        if (change.Property == KindProperty)
        {
            UpdateKindClasses();
        }
    }

    private void UpdateKindClasses()
    {
        PseudoClasses.Set(":info", Kind == ToastKind.Info);
        PseudoClasses.Set(":success", Kind == ToastKind.Success);
        PseudoClasses.Set(":warning", Kind == ToastKind.Warning);
        PseudoClasses.Set(":error", Kind == ToastKind.Error);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
