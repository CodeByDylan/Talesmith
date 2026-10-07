using Microsoft.Extensions.Logging;
using Talesmith.Plugins;
using Talesmith.Runtime.Hosting;
using Talesmith.Scripting;

namespace Talesmith.Avalonia.Hosting;

/// <summary>How <see cref="DesktopGame"/> starts a game.</summary>
public sealed record DesktopGameOptions
{
    /// <summary>The folder containing the game's assets, with <c>config/game.json</c> inside.</summary>
    public required string AssetRoot { get; init; }

    /// <summary>Overrides the renderer from the game's settings.</summary>
    public RendererPreference? Renderer { get; init; }

    /// <summary>Enables the performance overlay, debug views, profiler reports and screenshots behind F3, F4, F9 and F12.</summary>
    public bool DeveloperTools { get; init; } = true;

    /// <summary>Asks Avalonia to composite the window with Vulkan on Linux, so a Vulkan renderer can hand frames to it as shared GPU images.</summary>
    /// <remarks>Avalonia falls back to OpenGL when Vulkan is unavailable. With this on, the host also sets <c>MESA_VK_WSI_PRESENT_MODE=fifo</c>
    /// before Avalonia starts, unless it is already set.</remarks>
    public bool VulkanCompositing { get; init; } = true;

    /// <summary>Which thread runs the game's frames in its window; a dedicated simulation thread by default, so UI stalls do not delay the game.</summary>
    public GameThreading Threading { get; init; } = GameThreading.Dedicated;

    /// <summary>Plays sound through OpenAL; when false the game is silent.</summary>
    public bool Audio { get; init; } = true;

    /// <summary>Where profiler reports and screenshots are saved.</summary>
    public string CaptureDirectory { get; init; } = Path.Combine(Environment.CurrentDirectory, "captures");

    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;

    /// <summary>Configures plugins from a manager that keeps them loaded across sessions, as the editor does; null loads them for this game only.</summary>
    public PluginManager? PluginManager { get; init; }

    /// <summary>The compiled scripts to run, such as the editor's latest compilation; null loads them from the game folder when it has them.</summary>
    public ScriptAssembly? Scripts { get; init; }

    /// <summary>Configures the game further after plugins were loaded and before it is built.</summary>
    public Action<GameBuilder>? Configure { get; init; }
}
