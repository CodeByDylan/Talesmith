using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Events;

namespace Talesmith.Input.Tests;

public sealed class InputServiceTests
{
    private readonly EventBus _events = new();
    private readonly InputService _input;

    public InputServiceTests() => _input = new InputService(_events);

    [Fact]
    public void KeysChangeOnlyWhenTheFrameAdvances()
    {
        _input.KeyDown(Key.Space, KeyModifiers.None);
        Assert.False(_input.IsDown(Key.Space));

        _input.Update();
        Assert.True(_input.IsDown(Key.Space));
        Assert.True(_input.WasPressed(Key.Space));

        _input.Update();
        Assert.True(_input.IsDown(Key.Space));
        Assert.False(_input.WasPressed(Key.Space));

        _input.KeyUp(Key.Space, KeyModifiers.None);
        _input.Update();
        Assert.False(_input.IsDown(Key.Space));
        Assert.True(_input.WasReleased(Key.Space));
    }

    [Fact]
    public void ModifiersComeFromHeldKeysNotFromTheHost()
    {
        var pressed = new List<KeyPressed>();
        using var subscription = _events.Subscribe((ref KeyPressed e) => pressed.Add(e));

        _input.KeyDown(Key.RightControl, KeyModifiers.None);
        _input.KeyDown(Key.S, KeyModifiers.Alt);
        _input.Update();

        Assert.Equal(KeyModifiers.Control, _input.Modifiers);
        Assert.Equal([new KeyPressed(Key.RightControl, KeyModifiers.Control), new KeyPressed(Key.S, KeyModifiers.Control)], pressed);

        _input.KeyUp(Key.RightControl, KeyModifiers.None);
        _input.Update();
        Assert.Equal(KeyModifiers.None, _input.Modifiers);
    }

    [Fact]
    public void PointerMovementAndWheelAddUpWithinAFrame()
    {
        _input.MouseMove(new Vector2(10, 10));
        _input.Update();
        Assert.Equal(new Vector2(10, 10), _input.MousePosition);
        Assert.Equal(Vector2.Zero, _input.MouseDelta);

        _input.MouseMove(new Vector2(15, 20));
        _input.MouseDown(MouseButton.Left, new Vector2(20, 30));
        _input.MouseWheel(new Vector2(0, 1));
        _input.MouseWheel(new Vector2(0, 1));
        _input.Update();
        Assert.Equal(new Vector2(20, 30), _input.MousePosition);
        Assert.Equal(new Vector2(10, 20), _input.MouseDelta);
        Assert.Equal(new Vector2(0, 2), _input.WheelDelta);
        Assert.True(_input.WasPressed(MouseButton.Left));

        _input.Update();
        Assert.Equal(Vector2.Zero, _input.MouseDelta);
        Assert.Equal(Vector2.Zero, _input.WheelDelta);
        Assert.True(_input.IsDown(MouseButton.Left));
    }

    [Fact]
    public void MouseButtonsPublishWhereThePointerWas()
    {
        var published = new List<object>();
        using var pressed = _events.Subscribe((ref MouseButtonPressed e) => published.Add(e));
        using var released = _events.Subscribe((ref MouseButtonReleased e) => published.Add(e));

        _input.MouseDown(MouseButton.Middle, new Vector2(3, 4));
        _input.MouseUp(MouseButton.Middle, new Vector2(6, 8));
        _input.Update();

        Assert.Equal([new MouseButtonPressed(MouseButton.Middle, new Vector2(3, 4)), new MouseButtonReleased(MouseButton.Middle, new Vector2(6, 8))], published);
        Assert.True(_input.WasPressed(MouseButton.Middle));
        Assert.True(_input.WasReleased(MouseButton.Middle));
        Assert.False(_input.IsDown(MouseButton.Middle));
    }

    [Fact]
    public void TextTypedLastsOneFrame()
    {
        _input.TextInput("h");
        _input.TextInput("");
        _input.TextInput("i");
        _input.Update();
        Assert.Equal("hi", _input.TextTyped);

        _input.Update();
        Assert.Equal("", _input.TextTyped);
    }

    [Fact]
    public void LosingFocusReleasesEverythingAndIgnoresInputUntilFocusReturns()
    {
        var releasedKeys = new List<Key>();
        var releasedButtons = new List<MouseButtonReleased>();
        using var keys = _events.Subscribe((ref KeyReleased e) => releasedKeys.Add(e.Key));
        using var buttons = _events.Subscribe((ref MouseButtonReleased e) => releasedButtons.Add(e));
        _input.KeyDown(Key.A, KeyModifiers.None);
        _input.MouseDown(MouseButton.Right, new Vector2(5, 5));
        _input.Update();

        _input.FocusChanged(false);
        _input.KeyDown(Key.B, KeyModifiers.None);
        _input.TextInput("b");
        _input.MouseWheel(new Vector2(0, 1));
        _input.MouseMove(new Vector2(50, 50));
        _input.Update();

        Assert.False(_input.IsEnabled);
        Assert.True(_input.WasReleased(Key.A));
        Assert.False(_input.IsDown(Key.B));
        Assert.Equal([Key.A], releasedKeys);
        Assert.Equal([new MouseButtonReleased(MouseButton.Right, new Vector2(5, 5))], releasedButtons);
        Assert.Equal("", _input.TextTyped);
        Assert.Equal(Vector2.Zero, _input.WheelDelta);
        Assert.Equal(Vector2.Zero, _input.MouseDelta);
        Assert.Equal(new Vector2(50, 50), _input.MousePosition);

        _input.FocusChanged(true);
        _input.KeyDown(Key.B, KeyModifiers.None);
        _input.Update();
        Assert.True(_input.IsEnabled);
        Assert.True(_input.WasPressed(Key.B));
    }

    [Fact]
    public void OneServiceIsRegisteredForEveryRole()
    {
        var services = new ServiceCollection().AddSingleton<IEventBus, EventBus>();
        services.AddTalesmithInput().AddTalesmithInput();
        using var provider = services.BuildServiceProvider();

        var input = provider.GetRequiredService<InputService>();
        Assert.Same(input, provider.GetRequiredService<IInputService>());
        Assert.Same(input, provider.GetRequiredService<IInputSink>());
        Assert.Equal(4, services.Count);
    }
}
