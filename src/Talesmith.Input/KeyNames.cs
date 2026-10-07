namespace Talesmith.Input;

/// <summary>Converts key names, as written in input profiles, to <see cref="Key"/> values.</summary>
/// <remarks>
/// Accepts <see cref="Key"/> names and common aliases, case-insensitively: digits ("0".."9"), "Ctrl", "Shift", "Alt", "Win"
/// (the left-hand modifier keys), "Esc", "Return", "Del", "Ins", "PgUp", "PgDn", "Backtick" and punctuation characters such as "-" or "[".
/// </remarks>
public static class KeyNames
{
    private static readonly Dictionary<string, Key> ByName = CreateLookup();

    public static bool TryParse(string? name, out Key key)
    {
        if (name is not null && ByName.TryGetValue(name.Trim(), out key))
            return true;

        key = Key.None;
        return false;
    }

    /// <exception cref="FormatException">The name is not a known key.</exception>
    public static Key Parse(string name) =>
        TryParse(name, out var key) ? key : throw new FormatException($"Unknown key '{name}'.");

    private static Dictionary<string, Key> CreateLookup()
    {
        var lookup = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in Enum.GetValues<Key>())
            lookup[key.ToString()] = key;

        for (var digit = 0; digit <= 9; digit++)
        {
            lookup[digit.ToString(System.Globalization.CultureInfo.InvariantCulture)] = Key.D0 + digit;
            lookup[$"Num{digit}"] = Key.NumPad0 + digit;
        }

        AddAliases(lookup, Key.LeftControl, "Ctrl", "Control", "LeftCtrl", "LCtrl");
        AddAliases(lookup, Key.RightControl, "RightCtrl", "RCtrl");
        AddAliases(lookup, Key.LeftShift, "Shift", "LShift");
        AddAliases(lookup, Key.RightShift, "RShift");
        AddAliases(lookup, Key.LeftAlt, "Alt", "LAlt");
        AddAliases(lookup, Key.RightAlt, "RAlt", "AltGr");
        AddAliases(lookup, Key.LeftMeta, "Meta", "Win", "Super");
        AddAliases(lookup, Key.Escape, "Esc");
        AddAliases(lookup, Key.Enter, "Return");
        AddAliases(lookup, Key.Space, "Spacebar");
        AddAliases(lookup, Key.Backspace, "Back");
        AddAliases(lookup, Key.Delete, "Del");
        AddAliases(lookup, Key.Insert, "Ins");
        AddAliases(lookup, Key.PageUp, "PgUp");
        AddAliases(lookup, Key.PageDown, "PgDn");
        AddAliases(lookup, Key.Up, "ArrowUp");
        AddAliases(lookup, Key.Down, "ArrowDown");
        AddAliases(lookup, Key.Left, "ArrowLeft");
        AddAliases(lookup, Key.Right, "ArrowRight");
        AddAliases(lookup, Key.PrintScreen, "PrtSc");
        AddAliases(lookup, Key.Grave, "Backtick", "Tilde", "`");
        AddAliases(lookup, Key.Minus, "-");
        AddAliases(lookup, Key.Plus, "=", "Equals");
        AddAliases(lookup, Key.Comma, ",");
        AddAliases(lookup, Key.Period, ".");
        AddAliases(lookup, Key.Slash, "/");
        AddAliases(lookup, Key.Backslash, "\\");
        AddAliases(lookup, Key.Semicolon, ";");
        AddAliases(lookup, Key.Quote, "'", "Apostrophe");
        AddAliases(lookup, Key.OpenBracket, "[");
        AddAliases(lookup, Key.CloseBracket, "]");
        return lookup;
    }

    private static void AddAliases(Dictionary<string, Key> lookup, Key key, params string[] aliases)
    {
        foreach (var alias in aliases)
            lookup[alias] = key;
    }
}
