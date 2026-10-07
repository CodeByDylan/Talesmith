using Microsoft.Extensions.Logging;
using Talesmith.Diagnostics;
using Talesmith.Rendering.Skia;
using Talesmith.Rendering.Vulkan;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Creates the render backend a game asks for, taking into account how frames can reach the window.</summary>
/// <remarks>
/// With <see cref="RendererPreference.Auto"/>, Vulkan is used when it can share frames with the window's compositor. When it cannot, Skia
/// draws on the compositor's GPU directly instead, and Vulkan only reads frames back to the window when the compositor works in software. An
/// explicit <see cref="RendererPreference.Vulkan"/> keeps Vulkan and reads frames back when sharing is impossible.
/// </remarks>
internal static partial class RenderBackends
{
    /// <param name="profiler">The render thread's profiler.</param>
    /// <param name="window">What the window's compositor supports; <see cref="WindowGraphics.None"/> without a window.</param>
    /// <param name="reason">Why this backend was chosen, for <see cref="RenderBackendChanged"/>.</param>
    public static IRenderBackend Create(RendererPreference preference, Profiler profiler, ILoggerFactory loggers, WindowGraphics window, out string reason)
    {
        var logger = loggers.CreateLogger("Talesmith.Rendering");
        var unavailable = "Skia was asked for";
        if (preference != RendererPreference.Skia && TryCreateVulkan(window, profiler, logger, ref unavailable) is { } vulkan)
        {
            if (!window.HasWindow)
            {
                reason = "Vulkan renders without a window";
                return new VulkanBackend(vulkan, window, logger);
            }

            if (CanShare(vulkan, window, out var sharing))
            {
                LogVulkanShares(logger, vulkan.Info.Device);
                reason = "Vulkan shares frames with the window as GPU images";
                return new VulkanBackend(vulkan, window, logger);
            }

            if (preference == RendererPreference.Vulkan || !window.IsGpuComposited)
            {
                LogVulkanCopies(logger, vulkan.Info.Device, sharing);
                reason = $"Vulkan reads frames back through the CPU because {sharing}";
                return new VulkanBackend(vulkan, window, logger);
            }

            LogSkiaInstead(logger, sharing);
            vulkan.Dispose();
            reason = $"Skia draws on the window's GPU because Vulkan cannot share frames with it: {sharing}";
            return CreateSkia(profiler, logger);
        }

        var target = window.IsGpuComposited ? "the window's GPU" : "the CPU";
        LogSkia(logger, target);
        reason = $"Skia draws on {target}: {unavailable}";
        return CreateSkia(profiler, logger);
    }

    private static SkiaBackend CreateSkia(Profiler profiler, ILogger logger) =>
        new(new SkiaRenderer(new SkiaRendererOptions { Profiler = profiler, Logger = logger }));

    private static VulkanRenderer? TryCreateVulkan(WindowGraphics window, Profiler profiler, ILogger logger, ref string unavailable)
    {
        try
        {
            return VulkanRenderer.Create(new VulkanRendererOptions { Profiler = profiler, Logger = logger, DeviceUuid = window.Interop?.DeviceUuid });
        }
        catch (VulkanUnavailableException ex)
        {
            LogVulkanUnavailable(logger, ex.Message);
            unavailable = $"Vulkan is unavailable: {ex.Message}";
            return null;
        }
    }

    private static bool CanShare(VulkanRenderer vulkan, WindowGraphics window, out string reason)
    {
        if (!window.CanImportVulkanImages(out reason))
            return false;
        if (!vulkan.SupportsSharedImages)
        {
            reason = vulkan.SharedImagesUnsupportedReason ?? "the Vulkan driver cannot export images";
            return false;
        }

        if (vulkan.SharedImagesRequireDedicatedAllocation && !window.ImportsWithVulkan)
        {
            reason = "the driver shares images only as dedicated allocations, which the window's OpenGL compositor cannot import";
            return false;
        }

        if (window.Interop!.DeviceUuid is { Length: > 0 } windowUuid && vulkan.DeviceUuid is { } vulkanUuid && !windowUuid.AsSpan().SequenceEqual(vulkanUuid))
        {
            reason = "the window is drawn on a different GPU than Vulkan uses";
            return false;
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Rendering with Skia on {Target}")]
    private static partial void LogSkia(ILogger logger, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rendering with Vulkan on {Device}; frames go to the window as shared GPU images")]
    private static partial void LogVulkanShares(ILogger logger, string device);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rendering with Vulkan on {Device}; frames are read back through the CPU because {Reason}")]
    private static partial void LogVulkanCopies(ILogger logger, string device, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rendering with Skia on the window's GPU, because Vulkan cannot share frames with it: {Reason}")]
    private static partial void LogSkiaInstead(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vulkan is unavailable, so Skia renders instead: {Reason}")]
    private static partial void LogVulkanUnavailable(ILogger logger, string reason);
}
