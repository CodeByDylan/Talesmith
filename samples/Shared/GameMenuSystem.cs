using Talesmith.Input;
using Talesmith.Systems;

namespace Talesmith.Samples.Shared;

/// <summary>Opens the pause menu when the player presses the "Menu" action; the menu itself handles closing.</summary>
[UpdateIn(SystemPhase.PreUpdate)]
public sealed class GameMenuSystem(IInputService input, GameMenu menu) : ISystem
{
    public const string MenuAction = "Menu";

    public void Update(in SystemContext context)
    {
        if (input.Actions.TryGet(MenuAction, out var action) && action!.WasPressed)
            menu.RequestOpen();
    }
}
