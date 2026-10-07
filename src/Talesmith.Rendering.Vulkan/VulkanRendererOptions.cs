using Microsoft.Extensions.Logging;
using Talesmith.Diagnostics;

namespace Talesmith.Rendering.Vulkan;

/// <summary>How <see cref="VulkanRenderer.Create"/> picks a device and what it reports.</summary>
public sealed class VulkanRendererOptions
{
    /// <summary>Uses only devices whose name contains this text (case-insensitive); null picks the best device.</summary>
    /// <remarks>Without a filter, discrete GPUs are preferred over integrated, virtual and CPU devices.</remarks>
    public string? DeviceName { get; init; }

    /// <summary>Prefers the device with this UUID, such as the GPU another graphics API in the process uses, so images can be shared with it.</summary>
    public byte[]? DeviceUuid { get; init; }

    /// <summary>Uses only software (CPU) devices such as lavapipe.</summary>
    public bool UseCpuDevice { get; init; }

    /// <summary>Enables the Khronos validation layer when it is installed; also enabled by TALESMITH_VULKAN_VALIDATION=1.</summary>
    public bool EnableValidation { get; init; }

    /// <summary>The render thread's profiler, which receives Vulkan markers and counters.</summary>
    public Profiler? Profiler { get; init; }

    /// <summary>Receives validation messages and shaders that fail to load.</summary>
    public ILogger? Logger { get; init; }

    /// <summary>How many frames the CPU may record ahead of the GPU.</summary>
    public int FramesInFlight { get; init; } = 2;
}
