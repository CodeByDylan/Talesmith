using Talesmith.Input;
using Talesmith.Systems;

namespace Talesmith.Samples.Cutscenes;

/// <summary>Reveals dialogue over time and lets the player continue and pick choices with the keyboard.</summary>
[UpdateIn(SystemPhase.PreUpdate)]
public sealed class DialogueInputSystem(DialogueState dialogue, IInputService input) : ISystem
{
    public void Update(in SystemContext context)
    {
        if (!dialogue.IsOpen)
            return;

        dialogue.Tick(context.Time.UnscaledDeltaTime);
        if (input.WasPressed(Key.Up) || input.WasPressed(Key.W))
            dialogue.MoveSelection(-1);
        if (input.WasPressed(Key.Down) || input.WasPressed(Key.S))
            dialogue.MoveSelection(1);
        for (var i = 0; i < 9; i++)
        {
            if (input.WasPressed(Key.D1 + i))
                dialogue.Choose(i);
        }

        if (input.Actions.TryGet("Interact", out var interact) && interact!.WasPressed)
            dialogue.Advance();
    }
}
