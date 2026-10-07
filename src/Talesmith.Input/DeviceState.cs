namespace Talesmith.Input;

/// <summary>Held, pressed and released flags for every key and mouse button in the current frame.</summary>
internal sealed class DeviceState
{
    public static readonly int KeyCount = (int)Enum.GetValues<Key>().Max() + 1;
    public static readonly int ButtonCount = (int)Enum.GetValues<MouseButton>().Max() + 1;

    private readonly bool[] _keyDown = new bool[KeyCount];
    private readonly bool[] _keyPressed = new bool[KeyCount];
    private readonly bool[] _keyReleased = new bool[KeyCount];
    private readonly bool[] _buttonDown = new bool[ButtonCount];
    private readonly bool[] _buttonPressed = new bool[ButtonCount];
    private readonly bool[] _buttonReleased = new bool[ButtonCount];

    public KeyModifiers Modifiers { get; private set; }

    public bool IsDown(Key key) => IsValid(key) && _keyDown[(int)key];

    public bool WasPressed(Key key) => IsValid(key) && _keyPressed[(int)key];

    public bool WasReleased(Key key) => IsValid(key) && _keyReleased[(int)key];

    public bool IsDown(MouseButton button) => IsValid(button) && _buttonDown[(int)button];

    public bool WasPressed(MouseButton button) => IsValid(button) && _buttonPressed[(int)button];

    public bool WasReleased(MouseButton button) => IsValid(button) && _buttonReleased[(int)button];

    public void BeginFrame()
    {
        Array.Clear(_keyPressed);
        Array.Clear(_keyReleased);
        Array.Clear(_buttonPressed);
        Array.Clear(_buttonReleased);
    }

    /// <summary>Marks a key held; returns false when it was already held or is not a valid key.</summary>
    public bool Press(Key key)
    {
        if (!IsValid(key) || key == Key.None || _keyDown[(int)key])
            return false;

        _keyDown[(int)key] = true;
        _keyPressed[(int)key] = true;
        UpdateModifiers();
        return true;
    }

    /// <summary>Marks a key released; returns false when it was not held.</summary>
    public bool Release(Key key)
    {
        if (!IsDown(key))
            return false;

        _keyDown[(int)key] = false;
        _keyReleased[(int)key] = true;
        UpdateModifiers();
        return true;
    }

    public bool Press(MouseButton button)
    {
        if (!IsValid(button) || _buttonDown[(int)button])
            return false;

        _buttonDown[(int)button] = true;
        _buttonPressed[(int)button] = true;
        return true;
    }

    public bool Release(MouseButton button)
    {
        if (!IsDown(button))
            return false;

        _buttonDown[(int)button] = false;
        _buttonReleased[(int)button] = true;
        return true;
    }

    private void UpdateModifiers()
    {
        var modifiers = KeyModifiers.None;
        if (_keyDown[(int)Key.LeftShift] || _keyDown[(int)Key.RightShift])
            modifiers |= KeyModifiers.Shift;
        if (_keyDown[(int)Key.LeftControl] || _keyDown[(int)Key.RightControl])
            modifiers |= KeyModifiers.Control;
        if (_keyDown[(int)Key.LeftAlt] || _keyDown[(int)Key.RightAlt])
            modifiers |= KeyModifiers.Alt;
        if (_keyDown[(int)Key.LeftMeta] || _keyDown[(int)Key.RightMeta])
            modifiers |= KeyModifiers.Meta;
        Modifiers = modifiers;
    }

    private static bool IsValid(Key key) => (uint)key < (uint)KeyCount;

    private static bool IsValid(MouseButton button) => (uint)button < (uint)ButtonCount;
}
