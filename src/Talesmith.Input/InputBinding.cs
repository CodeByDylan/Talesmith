using System.Numerics;
using System.Text.Json.Serialization;

namespace Talesmith.Input;

/// <summary>A physical input that drives an <see cref="InputAction"/>.</summary>
/// <remarks>Directions follow screen space: right is +X and down is +Y.</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(KeyBinding), "key")]
[JsonDerivedType(typeof(MouseButtonBinding), "mouse")]
[JsonDerivedType(typeof(AxisBinding), "axis")]
[JsonDerivedType(typeof(VectorBinding), "vector")]
public abstract record InputBinding
{
    private protected InputBinding()
    {
    }

    /// <summary>Whether any input of the binding is held.</summary>
    internal abstract bool IsDown(DeviceState state);

    /// <summary>Whether any input of the binding went down this frame.</summary>
    internal abstract bool WasPressed(DeviceState state);

    /// <summary>The binding's contribution to an axis, from -1 to 1.</summary>
    internal abstract float Axis(DeviceState state);

    /// <summary>The binding's contribution to a vector, each component from -1 to 1.</summary>
    internal abstract Vector2 Vector(DeviceState state);

    internal abstract bool Uses(Key key);

    internal virtual bool Uses(MouseButton button) => false;

    /// <summary>Whether this binding and another share a key or button.</summary>
    internal abstract bool Overlaps(InputBinding other);

    private protected static float Direction(DeviceState state, Key negative, Key positive) =>
        (state.IsDown(positive) ? 1 : 0) - (state.IsDown(negative) ? 1 : 0);
}

/// <summary>A key, optionally with modifiers that must be held; on an axis it counts as +1.</summary>
public sealed record KeyBinding(
    Key Key,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] KeyModifiers Modifiers = KeyModifiers.None) : InputBinding
{
    internal override bool IsDown(DeviceState state) => state.IsDown(Key) && HasModifiers(state);

    internal override bool WasPressed(DeviceState state) => state.WasPressed(Key) && HasModifiers(state);

    internal override float Axis(DeviceState state) => IsDown(state) ? 1 : 0;

    internal override Vector2 Vector(DeviceState state) => Vector2.Zero;

    internal override bool Uses(Key key) => key == Key;

    internal override bool Overlaps(InputBinding other) => other.Uses(Key);

    private bool HasModifiers(DeviceState state) => (state.Modifiers & Modifiers) == Modifiers;
}

/// <summary>A mouse button; on an axis it counts as +1.</summary>
public sealed record MouseButtonBinding(MouseButton Button) : InputBinding
{
    internal override bool IsDown(DeviceState state) => state.IsDown(Button);

    internal override bool WasPressed(DeviceState state) => state.WasPressed(Button);

    internal override float Axis(DeviceState state) => IsDown(state) ? 1 : 0;

    internal override Vector2 Vector(DeviceState state) => Vector2.Zero;

    internal override bool Uses(Key key) => false;

    internal override bool Uses(MouseButton button) => button == Button;

    internal override bool Overlaps(InputBinding other) => other.Uses(Button);
}

/// <summary>Two keys forming an axis from -1 to 1; on a vector it drives X.</summary>
public sealed record AxisBinding(Key Negative, Key Positive) : InputBinding
{
    internal override bool IsDown(DeviceState state) => state.IsDown(Negative) || state.IsDown(Positive);

    internal override bool WasPressed(DeviceState state) => state.WasPressed(Negative) || state.WasPressed(Positive);

    internal override float Axis(DeviceState state) => Direction(state, Negative, Positive);

    internal override Vector2 Vector(DeviceState state) => new(Axis(state), 0);

    internal override bool Uses(Key key) => key == Negative || key == Positive;

    internal override bool Overlaps(InputBinding other) => other.Uses(Negative) || other.Uses(Positive);
}

/// <summary>Four keys forming a direction; on an axis it drives the horizontal component.</summary>
public sealed record VectorBinding(Key Up, Key Down, Key Left, Key Right) : InputBinding
{
    internal override bool IsDown(DeviceState state) =>
        state.IsDown(Up) || state.IsDown(Down) || state.IsDown(Left) || state.IsDown(Right);

    internal override bool WasPressed(DeviceState state) =>
        state.WasPressed(Up) || state.WasPressed(Down) || state.WasPressed(Left) || state.WasPressed(Right);

    internal override float Axis(DeviceState state) => Direction(state, Left, Right);

    internal override Vector2 Vector(DeviceState state) => new(Direction(state, Left, Right), Direction(state, Up, Down));

    internal override bool Uses(Key key) => key == Up || key == Down || key == Left || key == Right;

    internal override bool Overlaps(InputBinding other) => other.Uses(Up) || other.Uses(Down) || other.Uses(Left) || other.Uses(Right);
}
