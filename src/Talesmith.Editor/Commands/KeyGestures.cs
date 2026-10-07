using Avalonia.Input;

namespace Talesmith.Editor.Commands;

/// <summary>Parses and formats keyboard shortcuts such as "Ctrl+Shift+P".</summary>
public static class KeyGestures
{
    /// <summary>Parses a shortcut, or returns null for an empty or invalid one.</summary>
    public static KeyGesture? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            return KeyGesture.Parse(text.Trim());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static string Format(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var parts = new List<string>(4);
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
            parts.Add("Ctrl");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
            parts.Add("Meta");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
            parts.Add("Alt");
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
            parts.Add("Shift");
        parts.Add(FormatKey(gesture.Key));
        return string.Join('+', parts);
    }

    /// <summary>Whether a shortcut should reach the editor while a game or text field has focus: it uses Ctrl, Alt or Meta, or a function key.</summary>
    public static bool IsGlobal(KeyGesture gesture) =>
        (gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0 || gesture.Key is >= Key.F1 and <= Key.F24;

    private static string FormatKey(Key key) => key switch
    {
        Key.OemPlus => "=",
        Key.OemMinus => "-",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.Oem2 => "/",
        Key.Delete => "Del",
        Key.Escape => "Esc",
        Key.Back => "Backspace",
        Key.Return => "Enter",
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => key.ToString()
    };
}
