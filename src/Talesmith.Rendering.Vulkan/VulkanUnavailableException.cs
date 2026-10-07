namespace Talesmith.Rendering.Vulkan;

/// <summary>Thrown by <see cref="VulkanRenderer.Create"/> when Vulkan or a usable device is missing; fall back to another renderer.</summary>
public sealed class VulkanUnavailableException : Exception
{
    public VulkanUnavailableException()
    {
    }

    public VulkanUnavailableException(string message)
        : base(message)
    {
    }

    public VulkanUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
