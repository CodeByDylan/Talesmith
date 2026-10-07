namespace Talesmith.Input;

/// <summary>A keyboard key, independent of the windowing toolkit.</summary>
public enum Key
{
    None = 0,
    A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Up, Down, Left, Right,
    Space, Enter, Escape, Tab, Backspace, Delete, Insert, Home, End, PageUp, PageDown,
    LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt, LeftMeta, RightMeta,
    NumPad0, NumPad1, NumPad2, NumPad3, NumPad4, NumPad5, NumPad6, NumPad7, NumPad8, NumPad9,
    Add, Subtract, Multiply, Divide, Decimal,
    Minus, Plus, Comma, Period, Slash, Backslash, Semicolon, Quote, OpenBracket, CloseBracket, Grave,
    CapsLock, PrintScreen, Pause
}

/// <summary>A mouse button.</summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
    XButton1,
    XButton2
}

/// <summary>Modifier keys held during an input event.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Meta = 8
}
