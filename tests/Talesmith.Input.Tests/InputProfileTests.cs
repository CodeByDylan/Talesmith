using System.Text.Json;

namespace Talesmith.Input.Tests;

public sealed class InputProfileTests
{
    [Fact]
    public void ProfilesReadEveryBindingTypeAndKeyAlias()
    {
        var profile = InputProfile.FromJson("""
            {
              // Written by hand, so comments and trailing commas are fine.
              "actions": {
                "interact": { "kind": "button", "bindings": [{ "type": "key", "key": "e" }, { "button": "Right", "type": "mouse" }] },
                "Save": { "kind": "button", "bindings": [{ "type": "key", "key": "S", "modifiers": "Control, Shift" }] },
                "Zoom": { "kind": "axis", "bindings": [{ "type": "axis", "negative": "-", "positive": "=" }] },
                "Move": { "kind": "vector", "bindings": [{ "type": "vector", "up": "ArrowUp", "down": "ArrowDown", "left": "a", "right": "d" }] },
              },
            }
            """);

        Assert.Equal([new KeyBinding(Key.E), new MouseButtonBinding(MouseButton.Right)], profile.Actions["Interact"].Bindings);
        Assert.Equal([new KeyBinding(Key.S, KeyModifiers.Control | KeyModifiers.Shift)], profile.Actions["save"].Bindings);
        Assert.Equal(InputActionKind.Axis, profile.Actions["Zoom"].Kind);
        Assert.Equal([new AxisBinding(Key.Minus, Key.Plus)], profile.Actions["Zoom"].Bindings);
        Assert.Equal(InputActionKind.Vector, profile.Actions["Move"].Kind);
        Assert.Equal([new VectorBinding(Key.Up, Key.Down, Key.A, Key.D)], profile.Actions["Move"].Bindings);
    }

    [Fact]
    public void ProfilesRoundTripThroughJson()
    {
        var map = new InputActionMap();
        map.Define("Move", InputActionKind.Vector, new VectorBinding(Key.W, Key.S, Key.A, Key.D));
        map.Define("Save", InputActionKind.Button, new KeyBinding(Key.S, KeyModifiers.Control), new MouseButtonBinding(MouseButton.XButton1));
        map.Define("Zoom", InputActionKind.Axis, new AxisBinding(Key.Minus, Key.Plus));

        var loaded = new InputActionMap(InputProfile.FromJson(map.ToProfile().ToJson()));

        Assert.Equal(map.Count, loaded.Count);
        foreach (var action in map)
        {
            Assert.Equal(action.Kind, loaded[action.Name].Kind);
            Assert.Equal(action.Bindings, loaded[action.Name].Bindings);
        }
    }

    [Theory]
    [InlineData("""{"actions":{"Jump":{"kind":"button","bindings":[{"type":"key","key":"Hyper"}]}}}""", "Unknown key 'Hyper'")]
    [InlineData("""{"actions":{"Jump":{"kind":"button","bindings":[{"type":"pedal"}]}}}""", "pedal")]
    [InlineData("null", "empty")]
    public void InvalidProfilesThrowJsonExceptions(string json, string message)
    {
        var error = Assert.Throws<JsonException>(() => InputProfile.FromJson(json));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }
}
