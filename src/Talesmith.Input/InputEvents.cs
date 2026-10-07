using System.Numerics;

namespace Talesmith.Input;

/// <summary>Raised on the event bus when a key goes down.</summary>
public readonly record struct KeyPressed(Key Key, KeyModifiers Modifiers);

/// <summary>Raised on the event bus when a key goes up.</summary>
public readonly record struct KeyReleased(Key Key, KeyModifiers Modifiers);

/// <summary>Raised on the event bus when a mouse button goes down, with the pointer position in viewport pixels.</summary>
public readonly record struct MouseButtonPressed(MouseButton Button, Vector2 Position);

public readonly record struct MouseButtonReleased(MouseButton Button, Vector2 Position);

/// <summary>Raised on the event bus when an action becomes active, such as "Interact" being pressed.</summary>
public readonly record struct ActionTriggered(string Action);

/// <summary>Raised on the event bus when an action stops being active.</summary>
public readonly record struct ActionReleased(string Action);
