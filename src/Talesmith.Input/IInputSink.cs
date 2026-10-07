using System.Numerics;

namespace Talesmith.Input;

/// <summary>Receives raw input from the host window. Hosts call these from any thread, usually their UI thread, as events arrive.</summary>
/// <remarks>Events are buffered and applied on the game thread at the start of the next frame.</remarks>
public interface IInputSink
{
    void KeyDown(Key key, KeyModifiers modifiers);

    void KeyUp(Key key, KeyModifiers modifiers);

    /// <summary>Text typed by the user, after keyboard layout and composition.</summary>
    void TextInput(string text);

    /// <summary>The pointer moved to a position in viewport pixels.</summary>
    void MouseMove(Vector2 position);

    void MouseDown(MouseButton button, Vector2 position);

    void MouseUp(MouseButton button, Vector2 position);

    /// <summary>The wheel turned; positive Y scrolls up, in notches.</summary>
    void MouseWheel(Vector2 delta);

    /// <summary>Whether the game view has keyboard focus. Losing focus releases every held key and button.</summary>
    void FocusChanged(bool hasFocus);
}
