using Talesmith.Input;
using AvaloniaKey = Avalonia.Input.Key;
using AvaloniaModifiers = Avalonia.Input.KeyModifiers;

namespace Talesmith.Avalonia.Input;

/// <summary>Translates Avalonia keys and modifiers to the engine's.</summary>
public static class KeyMapping
{
    private static readonly Dictionary<AvaloniaKey, Key> Keys = Build();

    public static Key ToKey(AvaloniaKey key) => Keys.GetValueOrDefault(key, Key.None);

    public static KeyModifiers ToModifiers(AvaloniaModifiers modifiers) =>
        (modifiers.HasFlag(AvaloniaModifiers.Shift) ? KeyModifiers.Shift : KeyModifiers.None)
        | (modifiers.HasFlag(AvaloniaModifiers.Control) ? KeyModifiers.Control : KeyModifiers.None)
        | (modifiers.HasFlag(AvaloniaModifiers.Alt) ? KeyModifiers.Alt : KeyModifiers.None)
        | (modifiers.HasFlag(AvaloniaModifiers.Meta) ? KeyModifiers.Meta : KeyModifiers.None);

    private static Dictionary<AvaloniaKey, Key> Build()
    {
        var map = new Dictionary<AvaloniaKey, Key>();
        for (var i = 0; i < 26; i++)
            map[AvaloniaKey.A + i] = Key.A + i;
        for (var i = 0; i < 10; i++)
        {
            map[AvaloniaKey.D0 + i] = Key.D0 + i;
            map[AvaloniaKey.NumPad0 + i] = Key.NumPad0 + i;
        }

        for (var i = 0; i < 12; i++)
            map[AvaloniaKey.F1 + i] = Key.F1 + i;

        (AvaloniaKey From, Key To)[] named =
        [
            (AvaloniaKey.Up, Key.Up), (AvaloniaKey.Down, Key.Down), (AvaloniaKey.Left, Key.Left), (AvaloniaKey.Right, Key.Right),
            (AvaloniaKey.Space, Key.Space), (AvaloniaKey.Enter, Key.Enter), (AvaloniaKey.Escape, Key.Escape), (AvaloniaKey.Tab, Key.Tab),
            (AvaloniaKey.Back, Key.Backspace), (AvaloniaKey.Delete, Key.Delete), (AvaloniaKey.Insert, Key.Insert), (AvaloniaKey.Home, Key.Home),
            (AvaloniaKey.End, Key.End), (AvaloniaKey.PageUp, Key.PageUp), (AvaloniaKey.PageDown, Key.PageDown),
            (AvaloniaKey.LeftShift, Key.LeftShift), (AvaloniaKey.RightShift, Key.RightShift), (AvaloniaKey.LeftCtrl, Key.LeftControl),
            (AvaloniaKey.RightCtrl, Key.RightControl), (AvaloniaKey.LeftAlt, Key.LeftAlt), (AvaloniaKey.RightAlt, Key.RightAlt),
            (AvaloniaKey.LWin, Key.LeftMeta), (AvaloniaKey.RWin, Key.RightMeta),
            (AvaloniaKey.Add, Key.Add), (AvaloniaKey.Subtract, Key.Subtract), (AvaloniaKey.Multiply, Key.Multiply), (AvaloniaKey.Divide, Key.Divide),
            (AvaloniaKey.Decimal, Key.Decimal), (AvaloniaKey.OemMinus, Key.Minus), (AvaloniaKey.OemPlus, Key.Plus), (AvaloniaKey.OemComma, Key.Comma),
            (AvaloniaKey.OemPeriod, Key.Period), (AvaloniaKey.OemQuestion, Key.Slash), (AvaloniaKey.OemBackslash, Key.Backslash),
            (AvaloniaKey.OemPipe, Key.Backslash), (AvaloniaKey.OemSemicolon, Key.Semicolon), (AvaloniaKey.OemQuotes, Key.Quote),
            (AvaloniaKey.OemOpenBrackets, Key.OpenBracket), (AvaloniaKey.OemCloseBrackets, Key.CloseBracket), (AvaloniaKey.OemTilde, Key.Grave),
            (AvaloniaKey.CapsLock, Key.CapsLock), (AvaloniaKey.PrintScreen, Key.PrintScreen), (AvaloniaKey.Pause, Key.Pause)
        ];
        foreach (var (from, to) in named)
            map[from] = to;
        return map;
    }
}
