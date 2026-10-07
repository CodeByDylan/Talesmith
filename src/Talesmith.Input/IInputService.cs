using System.Numerics;

namespace Talesmith.Input;

/// <summary>Keyboard, mouse and action state for the current frame.</summary>
/// <remarks>
/// State changes once per frame, when the game loop calls <see cref="Update"/>, so every system sees the same input during a frame.
/// "Pressed" and "released" are true only in the frame the change happened. While <see cref="IsEnabled"/> is false, for example when
/// a menu has focus, nothing reads as held and actions do not trigger.
/// </remarks>
public interface IInputService
{
    /// <summary>Whether the game receives input; false while the game view does not have focus.</summary>
    bool IsEnabled { get; }

    bool IsDown(Key key);

    bool WasPressed(Key key);

    bool WasReleased(Key key);

    KeyModifiers Modifiers { get; }

    bool IsDown(MouseButton button);

    bool WasPressed(MouseButton button);

    bool WasReleased(MouseButton button);

    /// <summary>The pointer position in device pixels of the game view, with (0, 0) at its top-left; <c>RenderContext.ScreenToView</c> converts it to view units.</summary>
    Vector2 MousePosition { get; }

    /// <summary>How far the pointer moved since the previous frame, in pixels.</summary>
    Vector2 MouseDelta { get; }

    /// <summary>Wheel movement since the previous frame, in notches.</summary>
    Vector2 WheelDelta { get; }

    /// <summary>Text typed since the previous frame.</summary>
    string TextTyped { get; }

    /// <summary>The semantic actions of the active input profile.</summary>
    InputActionMap Actions { get; }

    /// <summary>Applies buffered events, advances to a new frame and raises input events on the event bus.</summary>
    void Update();
}
