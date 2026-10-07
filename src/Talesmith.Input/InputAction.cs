using System.Numerics;

namespace Talesmith.Input;

/// <summary>What an <see cref="InputAction"/> produces.</summary>
public enum InputActionKind
{
    /// <summary>On or off, such as "Interact".</summary>
    Button,

    /// <summary>A value from -1 to 1, such as "Zoom".</summary>
    Axis,

    /// <summary>A direction with length up to 1, such as "Move".</summary>
    Vector
}

/// <summary>A named, rebindable game action and its state for the current frame.</summary>
/// <remarks>
/// Every kind reports <see cref="IsDown"/> while any binding is held, so axes and vectors also raise
/// <see cref="ActionTriggered"/> and <see cref="ActionReleased"/>.
/// </remarks>
public sealed class InputAction
{
    private InputBinding[] _defaultBindings;
    private InputBinding[] _bindings;

    internal InputAction(string name, InputActionKind kind, InputBinding[] defaultBindings)
    {
        Name = name;
        Kind = kind;
        _defaultBindings = defaultBindings;
        _bindings = defaultBindings;
    }

    public string Name { get; }

    public InputActionKind Kind { get; private set; }

    /// <summary>The bindings currently driving the action.</summary>
    public IReadOnlyList<InputBinding> Bindings => _bindings;

    /// <summary>The bindings restored by <see cref="InputActionMap.ResetToDefaults"/>.</summary>
    public IReadOnlyList<InputBinding> DefaultBindings => _defaultBindings;

    public bool IsDown { get; private set; }

    /// <summary>Whether the action became active this frame, including a press and release within the frame.</summary>
    public bool WasPressed { get; private set; }

    /// <summary>Whether the action stopped being active this frame.</summary>
    public bool WasReleased { get; private set; }

    /// <summary>The value of an axis action, from -1 to 1; 0 for other kinds.</summary>
    public float Value { get; private set; }

    /// <summary>The direction of a vector action in screen space (down is +Y), normalized when longer than 1; zero for other kinds.</summary>
    public Vector2 Vector { get; private set; }

    internal void SetDefaults(InputActionKind kind, InputBinding[] bindings)
    {
        Kind = kind;
        _defaultBindings = bindings;
        _bindings = bindings;
    }

    internal void SetBindings(InputActionKind kind, InputBinding[] bindings)
    {
        Kind = kind;
        _bindings = bindings;
    }

    internal void ReplaceBinding(int index, InputBinding binding)
    {
        var bindings = new InputBinding[Math.Max(_bindings.Length, index + 1)];
        _bindings.CopyTo(bindings, 0);
        bindings[index] = binding;
        _bindings = bindings;
    }

    internal void ResetToDefaults() => _bindings = _defaultBindings;

    internal void Update(DeviceState state)
    {
        var wasDown = IsDown;
        var down = false;
        var pressed = false;
        var value = 0f;
        var vector = Vector2.Zero;
        foreach (var binding in _bindings)
        {
            down |= binding.IsDown(state);
            pressed |= binding.WasPressed(state);
            if (Kind == InputActionKind.Axis)
                value += binding.Axis(state);
            else if (Kind == InputActionKind.Vector)
                vector += binding.Vector(state);
        }

        IsDown = down;
        WasPressed = !wasDown && (down || pressed);
        WasReleased = (wasDown || WasPressed) && !down;
        Value = Math.Clamp(value, -1, 1);
        Vector = vector.LengthSquared() > 1 ? Vector2.Normalize(vector) : vector;
    }
}
