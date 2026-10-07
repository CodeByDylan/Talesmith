using System.Globalization;
using Talesmith.Rendering;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.PlayMode;

/// <summary>A named window to preview the game in, such as Full HD.</summary>
public sealed record GameWindowPreset(string Name, GameWindowSize Size)
{
    /// <summary>The common windows: desktop sizes, other shapes, then handheld and mobile displays.</summary>
    public static IReadOnlyList<GameWindowPreset> Common { get; } =
    [
        new("HD", new GameWindowSize(1280, 720)),
        new("Laptop", new GameWindowSize(1366, 768)),
        new("Full HD", new GameWindowSize(1920, 1080)),
        new("QHD", new GameWindowSize(2560, 1440)),
        new("4K UHD", new GameWindowSize(3840, 2160)),
        new("WUXGA", new GameWindowSize(1920, 1200)),
        new("Ultrawide", new GameWindowSize(2560, 1080)),
        new("Ultrawide QHD", new GameWindowSize(3440, 1440)),
        new("XGA", new GameWindowSize(1024, 768)),
        new("Steam Deck", new GameWindowSize(1280, 800)),
        new("Tablet", new GameWindowSize(2048, 1536, 2)),
        new("Phone", new GameWindowSize(1170, 2532, 3))
    ];

    /// <summary>The size, shape and display scale, such as "1920 × 1080 · 16:9" or "1170 × 2532 · 9:19.5 · 3×".</summary>
    public string Details => Size.DisplayScale == 1
        ? $"{Size.Width} × {Size.Height} · {Size.AspectRatio}"
        : string.Create(CultureInfo.InvariantCulture, $"{Size.Width} × {Size.Height} · {Size.AspectRatio} · {Size.DisplayScale:0.##}×");

    /// <summary>The project's windows: the window the exported game opens and, with a scaled view, the view's design size when it differs.</summary>
    public static IEnumerable<GameWindowPreset> Of(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var window = new GameWindowSize(settings.WindowWidth, settings.WindowHeight).Clamped();
        yield return new GameWindowPreset("Game window", window);
        var design = new GameWindowSize(settings.View.Width, settings.View.Height).Clamped();
        if (settings.View.ScaleMode != ViewScaleMode.None && design != window)
            yield return new GameWindowPreset("Design size", design);
    }
}
