using Avalonia.Platform;
using Avalonia.Rendering.Composition;

namespace Talesmith.Avalonia.Presentation;

/// <summary>What Avalonia's compositor can do with GPU images, which decides how a renderer's frames reach the window.</summary>
public sealed class WindowGraphics
{
    private WindowGraphics(bool hasWindow, ICompositionGpuInterop? interop, bool importsWithVulkan)
    {
        HasWindow = hasWindow;
        Interop = interop;
        ImportsWithVulkan = importsWithVulkan;
    }

    /// <summary>For hosts without Avalonia windows, such as headless benchmarks.</summary>
    public static WindowGraphics None { get; } = new(false, null, false);

    /// <summary>Whether frames are shown in an Avalonia window at all.</summary>
    public bool HasWindow { get; }

    /// <summary>The compositor's GPU interop, or null when it composites in software.</summary>
    public ICompositionGpuInterop? Interop { get; }

    /// <summary>Whether the compositor draws on the GPU.</summary>
    public bool IsGpuComposited => Interop is not null;

    /// <summary>Whether the compositor imports images with Vulkan, which imports them as dedicated allocations; otherwise it uses OpenGL.</summary>
    public bool ImportsWithVulkan { get; }

    /// <summary>Asks the default compositor; call it on the UI thread once Avalonia is initialized.</summary>
    public static async Task<WindowGraphics> QueryAsync()
    {
        if (Compositor.TryGetDefaultCompositor() is not { } compositor)
            return None;
        var interop = await compositor.TryGetCompositionGpuInterop();
        if (interop is null or { IsLost: true })
            return new WindowGraphics(true, null, false);
        var importer = await compositor.TryGetRenderInterfaceFeature(typeof(IExternalObjectsRenderInterfaceContextFeature));
        // Avalonia has no public way to tell its graphics API apart; its Vulkan importer lives in this namespace.
        return new WindowGraphics(true, interop, importer?.GetType().Namespace == "Avalonia.Skia.Vulkan");
    }

    /// <summary>Whether the compositor imports Vulkan images and semaphores as POSIX file descriptors.</summary>
    public bool CanImportVulkanImages(out string reason)
    {
        const string handleType = KnownPlatformGraphicsExternalImageHandleTypes.VulkanOpaquePosixFileDescriptor;
        if (Interop is null)
        {
            reason = "the window is composited in software";
            return false;
        }

        if (!Interop.SupportedImageHandleTypes.Contains(handleType)
            || !Interop.SupportedSemaphoreTypes.Contains(KnownPlatformGraphicsExternalSemaphoreHandleTypes.VulkanOpaquePosixFileDescriptor)
            || !Interop.GetSynchronizationCapabilities(handleType).HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.Semaphores))
        {
            reason = "the window's graphics API cannot import Vulkan images";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
