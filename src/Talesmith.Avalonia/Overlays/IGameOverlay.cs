using Avalonia.Controls;

namespace Talesmith.Avalonia.Overlays;

/// <summary>Contributes an Avalonia control drawn above the game, such as a HUD, a dialogue box or a menu.</summary>
/// <remarks>
/// <para>Register implementations with dependency injection, for example from a plugin. Overlays are created once on the UI thread when the
/// game view is shown and stacked by <see cref="Order"/>; the game keeps receiving input unless an overlay control takes keyboard focus.</para>
/// <para>Overlays lay out on the game's view, scaled with it by a <see cref="GameOverlayLayer"/>, in view units or in the overlay size of
/// <see cref="Rendering.ViewSettings.OverlayWidth"/>: a control 640 units wide covers a 640×360 view, or half of it with a 1280×720 overlay
/// size, at any window size.</para>
/// <para>The game may run on its own simulation thread, so overlays never touch game state directly: they show
/// <see cref="Runtime.Hosting.ViewState{T}"/> snapshots that game code publishes, and send commands back with
/// <see cref="Runtime.Hosting.Game.Post"/>.</para>
/// </remarks>
public interface IGameOverlay
{
    /// <summary>Lower orders are placed below higher ones.</summary>
    int Order => 0;

    Control Create(IServiceProvider services);
}
