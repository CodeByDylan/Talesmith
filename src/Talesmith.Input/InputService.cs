using System.Numerics;
using System.Text;
using Talesmith.Events;

namespace Talesmith.Input;

/// <summary>The default <see cref="IInputService"/>: buffers raw input from the host and applies it once per frame.</summary>
/// <remarks>
/// Sink methods are thread-safe. <see cref="Update"/> and the queries belong to the game loop thread. Modifiers are derived from the
/// held modifier keys rather than taken from the host.
/// </remarks>
public sealed class InputService(IEventBus events) : IInputService, IInputSink
{
    private readonly Lock _pendingLock = new();
    private List<RawInput> _pending = [];
    private List<RawInput> _applying = [];
    private readonly DeviceState _state = new();
    private readonly StringBuilder _text = new();
    private bool _hasPointerPosition;

    public bool IsEnabled { get; private set; } = true;

    public KeyModifiers Modifiers => _state.Modifiers;

    public Vector2 MousePosition { get; private set; }

    public Vector2 MouseDelta { get; private set; }

    public Vector2 WheelDelta { get; private set; }

    public string TextTyped { get; private set; } = string.Empty;

    public InputActionMap Actions { get; } = new();

    public bool IsDown(Key key) => _state.IsDown(key);

    public bool WasPressed(Key key) => _state.WasPressed(key);

    public bool WasReleased(Key key) => _state.WasReleased(key);

    public bool IsDown(MouseButton button) => _state.IsDown(button);

    public bool WasPressed(MouseButton button) => _state.WasPressed(button);

    public bool WasReleased(MouseButton button) => _state.WasReleased(button);

    public void Update()
    {
        lock (_pendingLock)
            (_pending, _applying) = (_applying, _pending);

        _state.BeginFrame();
        MouseDelta = Vector2.Zero;
        WheelDelta = Vector2.Zero;
        _text.Clear();

        foreach (var input in _applying)
            Apply(input);
        _applying.Clear();

        TextTyped = _text.Length > 0 ? _text.ToString() : string.Empty;
        Actions.Update(_state, events);
    }

    public void KeyDown(Key key, KeyModifiers modifiers) => Buffer(new RawInput(RawInputKind.KeyDown) { Key = key });

    public void KeyUp(Key key, KeyModifiers modifiers) => Buffer(new RawInput(RawInputKind.KeyUp) { Key = key });

    public void TextInput(string text)
    {
        if (!string.IsNullOrEmpty(text))
            Buffer(new RawInput(RawInputKind.Text) { Text = text });
    }

    public void MouseMove(Vector2 position) => Buffer(new RawInput(RawInputKind.MouseMove) { Position = position });

    public void MouseDown(MouseButton button, Vector2 position) =>
        Buffer(new RawInput(RawInputKind.MouseDown) { Button = button, Position = position });

    public void MouseUp(MouseButton button, Vector2 position) =>
        Buffer(new RawInput(RawInputKind.MouseUp) { Button = button, Position = position });

    public void MouseWheel(Vector2 delta) => Buffer(new RawInput(RawInputKind.Wheel) { Position = delta });

    public void FocusChanged(bool hasFocus) =>
        Buffer(new RawInput(hasFocus ? RawInputKind.FocusGained : RawInputKind.FocusLost));

    private void Buffer(in RawInput input)
    {
        lock (_pendingLock)
            _pending.Add(input);
    }

    private void Apply(in RawInput input)
    {
        switch (input.Kind)
        {
            case RawInputKind.KeyDown when IsEnabled && _state.Press(input.Key):
                events.Publish(new KeyPressed(input.Key, _state.Modifiers));
                break;
            case RawInputKind.KeyUp:
                ReleaseKey(input.Key);
                break;
            case RawInputKind.Text when IsEnabled:
                _text.Append(input.Text);
                break;
            case RawInputKind.MouseMove:
                MovePointer(input.Position);
                break;
            case RawInputKind.MouseDown:
                MovePointer(input.Position);
                if (IsEnabled && _state.Press(input.Button))
                    events.Publish(new MouseButtonPressed(input.Button, input.Position));
                break;
            case RawInputKind.MouseUp:
                MovePointer(input.Position);
                ReleaseButton(input.Button);
                break;
            case RawInputKind.Wheel when IsEnabled:
                WheelDelta += input.Position;
                break;
            case RawInputKind.FocusGained:
                IsEnabled = true;
                break;
            case RawInputKind.FocusLost:
                ReleaseAll();
                IsEnabled = false;
                break;
        }
    }

    private void MovePointer(Vector2 position)
    {
        if (_hasPointerPosition && IsEnabled)
            MouseDelta += position - MousePosition;
        MousePosition = position;
        _hasPointerPosition = true;
    }

    private void ReleaseKey(Key key)
    {
        if (_state.Release(key))
            events.Publish(new KeyReleased(key, _state.Modifiers));
    }

    private void ReleaseButton(MouseButton button)
    {
        if (_state.Release(button))
            events.Publish(new MouseButtonReleased(button, MousePosition));
    }

    private void ReleaseAll()
    {
        for (var key = 0; key < DeviceState.KeyCount; key++)
            ReleaseKey((Key)key);
        for (var button = 0; button < DeviceState.ButtonCount; button++)
            ReleaseButton((MouseButton)button);
    }

    private enum RawInputKind
    {
        KeyDown,
        KeyUp,
        Text,
        MouseMove,
        MouseDown,
        MouseUp,
        Wheel,
        FocusGained,
        FocusLost
    }

    private readonly record struct RawInput(RawInputKind Kind)
    {
        public Key Key { get; init; }

        public MouseButton Button { get; init; }

        /// <summary>The pointer position, or the wheel delta for <see cref="RawInputKind.Wheel"/>.</summary>
        public Vector2 Position { get; init; }

        public string? Text { get; init; }
    }
}
