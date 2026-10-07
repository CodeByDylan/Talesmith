using Avalonia;
using Avalonia.Controls.Primitives;

namespace Talesmith.UI.Controls;

/// <summary>Displays a keyboard shortcut such as "Ctrl+Shift+Z" as a row of key caps.</summary>
public class ShortcutBadge : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ShortcutBadge, string?>(nameof(Text));

    public static readonly DirectProperty<ShortcutBadge, IReadOnlyList<string>> KeysProperty =
        AvaloniaProperty.RegisterDirect<ShortcutBadge, IReadOnlyList<string>>(nameof(Keys), o => o.Keys);

    private IReadOnlyList<string> _keys = [];

    /// <summary>Gets or sets the shortcut text, with keys separated by '+'.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Gets the individual keys parsed from <see cref="Text"/>.</summary>
    public IReadOnlyList<string> Keys
    {
        get => _keys;
        private set => SetAndRaise(KeysProperty, ref _keys, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            Keys = Split(Text);
        }
    }

    private static string[] Split(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // A trailing '+' denotes the plus key itself, as in "Ctrl++".
        var keys = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (text.EndsWith("++", StringComparison.Ordinal) || text == "+")
        {
            keys.Add("+");
        }

        return [.. keys];
    }
}
