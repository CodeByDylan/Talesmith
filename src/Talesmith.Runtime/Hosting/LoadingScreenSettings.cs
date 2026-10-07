using Talesmith.Mathematics;

namespace Talesmith.Runtime.Hosting;

/// <summary>What a game shows while it starts and while a scene takes long to load: <c>loadingScreen</c> in <c>game.json</c>.</summary>
public sealed record LoadingScreenSettings
{
    /// <summary>The color behind the image and the progress bar; null uses the game's <see cref="GameSettings.ClearColor"/>.</summary>
    public Color? BackgroundColor { get; init; }

    /// <summary>An image asset shown in the middle, such as the game's logo; without one, the game's title is shown.</summary>
    /// <remarks>Builds ship it with the game. When it is missing or cannot be read, the title is shown instead.</remarks>
    public string? Image { get; init; }

    /// <summary>The color of the title and the progress bar; null picks white or near-black, whichever stands out on the background.</summary>
    public Color? ForegroundColor { get; init; }

    /// <summary>Shows the loading screen when a scene change takes more than a moment, after the old scene has faded out.</summary>
    public bool BetweenScenes { get; init; } = true;
}
