using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Talesmith.UI.Controls;

/// <summary>A filter field with a search icon and a clear button; Escape clears the text.</summary>
[TemplatePart(TextBoxPart, typeof(TextBox))]
[TemplatePart(ClearPart, typeof(Button))]
[PseudoClasses(":has-text")]
public class SearchBox : TemplatedControl
{
    private const string TextBoxPart = "PART_TextBox";
    private const string ClearPart = "PART_Clear";

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchBox, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<SearchBox, string?>(nameof(PlaceholderText), "Search");

    private TextBox? _textBox;
    private Button? _clear;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Moves keyboard focus to the text and selects it.</summary>
    public void FocusInput()
    {
        _textBox?.Focus(NavigationMethod.Tab);
        _textBox?.SelectAll();
    }

    /// <summary>Clears the text.</summary>
    public void Clear() => SetCurrentValue(TextProperty, string.Empty);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _textBox?.RemoveHandler(KeyDownEvent, OnTextKeyDown);
        _clear?.Click -= OnClearClick;
        _textBox = e.NameScope.Find<TextBox>(TextBoxPart);
        _clear = e.NameScope.Find<Button>(ClearPart);
        _textBox?.AddHandler(KeyDownEvent, OnTextKeyDown, RoutingStrategies.Tunnel);
        _clear?.Click += OnClearClick;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (ReferenceEquals(e.Source, this))
            _textBox?.Focus(e.NavigationMethod);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            PseudoClasses.Set(":has-text", !string.IsNullOrEmpty(Text));
    }

    private void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || string.IsNullOrEmpty(Text))
            return;
        Clear();
        e.Handled = true;
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        Clear();
        _textBox?.Focus();
    }
}
