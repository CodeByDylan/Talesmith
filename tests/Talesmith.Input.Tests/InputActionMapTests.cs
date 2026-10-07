using System.Numerics;
using Talesmith.Events;

namespace Talesmith.Input.Tests;

public sealed class InputActionMapTests
{
    private readonly EventBus _events = new();
    private readonly InputService _input;

    public InputActionMapTests() => _input = new InputService(_events);

    [Fact]
    public void ButtonsTriggerOnceWhileAnyBindingIsHeld()
    {
        var log = new List<string>();
        using var triggered = _events.Subscribe((ref ActionTriggered e) => log.Add("triggered " + e.Action));
        using var released = _events.Subscribe((ref ActionReleased e) => log.Add("released " + e.Action));
        var jump = _input.Actions.Define("Jump", InputActionKind.Button, new KeyBinding(Key.Space), new KeyBinding(Key.W));

        Press(Key.Space);
        Assert.True(jump.WasPressed);
        Press(Key.W);
        Assert.False(jump.WasPressed);
        Release(Key.Space);
        Assert.True(jump.IsDown);
        Assert.False(jump.WasReleased);
        Release(Key.W);

        Assert.False(jump.IsDown);
        Assert.True(jump.WasReleased);
        Assert.Equal(["triggered Jump", "released Jump"], log);
    }

    [Fact]
    public void ATapWithinOneFrameTriggersAndReleases()
    {
        var jump = _input.Actions.Define("Jump", InputActionKind.Button, new KeyBinding(Key.Space));

        _input.KeyDown(Key.Space, KeyModifiers.None);
        _input.KeyUp(Key.Space, KeyModifiers.None);
        _input.Update();

        Assert.False(jump.IsDown);
        Assert.True(jump.WasPressed);
        Assert.True(jump.WasReleased);
    }

    [Fact]
    public void KeyBindingsWithModifiersNeedThemHeld()
    {
        var save = _input.Actions.Define("Save", InputActionKind.Button, new KeyBinding(Key.S, KeyModifiers.Control));

        Press(Key.S);
        Assert.False(save.IsDown);

        Release(Key.S);
        Press(Key.LeftControl);
        Press(Key.LeftShift);
        Press(Key.S);
        Assert.True(save.WasPressed);
    }

    [Fact]
    public void AxesAddTheirBindingsAndClamp()
    {
        var zoom = _input.Actions.Define("Zoom", InputActionKind.Axis, new AxisBinding(Key.Minus, Key.Plus), new KeyBinding(Key.PageUp));

        Press(Key.Plus);
        Assert.Equal(1, zoom.Value);
        Press(Key.Minus);
        Assert.Equal(0, zoom.Value);
        Release(Key.Minus);
        Press(Key.PageUp);
        Assert.Equal(1, zoom.Value);
        Assert.Equal(Vector2.Zero, zoom.Vector);
    }

    [Fact]
    public void VectorsPointDownTheScreenAndAreNeverLongerThanOne()
    {
        var move = _input.Actions.Define("Move", InputActionKind.Vector,
            new VectorBinding(Key.W, Key.S, Key.A, Key.D), new VectorBinding(Key.Up, Key.Down, Key.Left, Key.Right));

        Press(Key.S);
        Assert.Equal(new Vector2(0, 1), move.Vector);
        Press(Key.D);
        Assert.Equal(Vector2.Normalize(new Vector2(1, 1)), move.Vector);
        Release(Key.D);
        Press(Key.Down);
        Assert.Equal(new Vector2(0, 1), move.Vector);
        Assert.Equal(0, move.Value);
    }

    [Fact]
    public void RebindingChangesTheBindingsUntilTheyAreReset()
    {
        var map = _input.Actions;
        var jump = map.Define("Jump", InputActionKind.Button, new KeyBinding(Key.Space));

        map.Rebind("jump", 0, new KeyBinding(Key.W));
        map.Rebind("JUMP", 1, new MouseButtonBinding(MouseButton.Right));

        Assert.Equal([new KeyBinding(Key.W), new MouseButtonBinding(MouseButton.Right)], jump.Bindings);
        Assert.Equal([new KeyBinding(Key.Space)], jump.DefaultBindings);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Rebind("Jump", 3, new KeyBinding(Key.E)));
        Assert.Throws<KeyNotFoundException>(() => map.Rebind("Fly", 0, new KeyBinding(Key.E)));

        map.ResetToDefaults();
        Assert.Equal([new KeyBinding(Key.Space)], jump.Bindings);
    }

    [Fact]
    public void ConflictsAreTheActionsSharingAKeyOrButton()
    {
        var map = _input.Actions;
        var move = map.Define("Move", InputActionKind.Vector, new VectorBinding(Key.W, Key.S, Key.A, Key.D));
        var fire = map.Define("Fire", InputActionKind.Button, new MouseButtonBinding(MouseButton.Left), new KeyBinding(Key.Space));
        map.Define("Zoom", InputActionKind.Axis, new AxisBinding(Key.Minus, Key.Plus));

        Assert.Equal([move], map.FindConflicts(new KeyBinding(Key.A, KeyModifiers.Control)));
        Assert.Equal([fire], map.FindConflicts(new MouseButtonBinding(MouseButton.Left)));
        Assert.Equal([move, fire], map.FindConflicts(new VectorBinding(Key.Space, Key.S, Key.Left, Key.Right)));
        Assert.Empty(map.FindConflicts(new KeyBinding(Key.E)));
    }

    [Fact]
    public void ProfilesReplaceTheBindingsButNotTheDefaults()
    {
        var defaults = new InputProfile();
        defaults.Actions["Jump"] = new InputActionDefinition(InputActionKind.Button, [new KeyBinding(Key.Space)]);
        var map = new InputActionMap(defaults);
        var saved = new InputProfile();
        saved.Actions["jump"] = new InputActionDefinition(InputActionKind.Button, [new KeyBinding(Key.Up)]);
        saved.Actions["Dash"] = new InputActionDefinition(InputActionKind.Button, [new KeyBinding(Key.LeftShift)]);

        map.Apply(saved);

        Assert.Equal(2, map.Count);
        Assert.Equal([new KeyBinding(Key.Up)], map["Jump"].Bindings);
        Assert.Equal([new KeyBinding(Key.Space)], map["Jump"].DefaultBindings);
        Assert.Equal([new KeyBinding(Key.LeftShift)], map["dash"].Bindings);
        Assert.Equal([new KeyBinding(Key.Up)], map.ToProfile().Actions["Jump"].Bindings);
    }

    private void Press(Key key)
    {
        _input.KeyDown(key, KeyModifiers.None);
        _input.Update();
    }

    private void Release(Key key)
    {
        _input.KeyUp(key, KeyModifiers.None);
        _input.Update();
    }
}
