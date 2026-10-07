using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Talesmith.UI.Controls;

/// <summary>A centered placeholder for an empty panel or list: an icon, a title, a hint and an optional action button.</summary>
public class EmptyState : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<EmptyState, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Hint));

    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(ActionText));

    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<EmptyState, ICommand?>(nameof(ActionCommand));

    public static readonly StyledProperty<object?> ActionCommandParameterProperty =
        AvaloniaProperty.Register<EmptyState, object?>(nameof(ActionCommandParameter));

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

    /// <summary>Gets or sets the explanation under the title, such as what to do to fill the panel.</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>Gets or sets the label of the action button; the button is hidden when null.</summary>
    public string? ActionText
    {
        get => GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public object? ActionCommandParameter
    {
        get => GetValue(ActionCommandParameterProperty);
        set => SetValue(ActionCommandParameterProperty, value);
    }
}
