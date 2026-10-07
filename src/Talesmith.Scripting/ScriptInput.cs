using System.Numerics;
using Talesmith.Input;

namespace Talesmith.Scripting;

/// <summary>Keyboard, mouse and input actions for scripts; see <see cref="Script.Input"/>.</summary>
/// <remarks>
/// Prefer actions, which players can rebind, to keys: actions are declared in the game's input profile (<c>config/input.json</c>) and read
/// by name, such as <c>Input.Vector("Move")</c> or <c>Input.WasPressed("Jump")</c>. Unknown action names read as idle.
/// </remarks>
public readonly struct ScriptInput
{
    private readonly ScriptRuntime _runtime;

    internal ScriptInput(ScriptRuntime runtime) => _runtime = runtime;

    /// <summary>The input service, for anything not covered here, such as typed text or rebinding.</summary>
    public IInputService Service => _runtime.Input;

    /// <summary>Whether the game receives input; false while the game view does not have focus.</summary>
    public bool IsEnabled => Service.IsEnabled;

    public bool IsDown(Key key) => Service.IsDown(key);

    /// <summary>Whether the key went down this frame.</summary>
    public bool WasPressed(Key key) => Service.WasPressed(key);

    /// <summary>Whether the key went up this frame.</summary>
    public bool WasReleased(Key key) => Service.WasReleased(key);

    public bool IsDown(MouseButton button) => Service.IsDown(button);

    public bool WasPressed(MouseButton button) => Service.WasPressed(button);

    public bool WasReleased(MouseButton button) => Service.WasReleased(button);

    /// <summary>The pointer position in device pixels of the game view, with (0, 0) at its top-left.</summary>
    public Vector2 MousePosition => Service.MousePosition;

    /// <summary>The pointer position in view units, the space of HUDs drawn in <see cref="Rendering.RenderSpace.Screen"/>, as of the last frame.</summary>
    /// <remarks>Over the bars around the view it lies below 0 or beyond the view size.</remarks>
    public Vector2 MouseViewPosition => _runtime.Render.ScreenToView(Service.MousePosition);

    /// <summary>The pointer position in world units, seen through the camera of the last frame.</summary>
    public Vector2 MouseWorldPosition => _runtime.Render.ScreenToWorld(Service.MousePosition);

    /// <summary>Whether the pointer is over the game's view rather than the bars around it, as of the last frame.</summary>
    /// <remarks>Over the bars, <see cref="MouseWorldPosition"/> lies outside the visible area, so check this before acting on a click in the world.</remarks>
    public bool IsMouseOverView => _runtime.Render.View.Contains(Service.MousePosition);

    /// <summary>How far the pointer moved since the previous frame, in pixels.</summary>
    public Vector2 MouseDelta => Service.MouseDelta;

    /// <summary>Wheel movement since the previous frame, in notches.</summary>
    public Vector2 WheelDelta => Service.WheelDelta;

    /// <summary>Gets an action of the input profile by name, or null when the profile has none.</summary>
    public InputAction? Action(string name) => Service.Actions.TryGet(name, out var action) ? action : null;

    /// <summary>Whether an action is held.</summary>
    public bool IsDown(string action) => Action(action)?.IsDown ?? false;

    /// <summary>Whether an action became active this frame.</summary>
    public bool WasPressed(string action) => Action(action)?.WasPressed ?? false;

    /// <summary>Whether an action stopped being active this frame.</summary>
    public bool WasReleased(string action) => Action(action)?.WasReleased ?? false;

    /// <summary>The value of an axis action, from -1 to 1.</summary>
    public float Axis(string action) => Action(action)?.Value ?? 0;

    /// <summary>The direction of a vector action, with length up to 1; down is +Y.</summary>
    public Vector2 Vector(string action) => Action(action)?.Vector ?? Vector2.Zero;
}
