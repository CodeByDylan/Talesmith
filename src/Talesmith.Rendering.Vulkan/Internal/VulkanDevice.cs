using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>The Vulkan instance, the chosen physical device and its logical device with one graphics queue.</summary>
internal sealed unsafe partial class VulkanDevice : IDisposable
{
    /// <summary>The format of images shared with other graphics APIs, which import them as RGBA8.</summary>
    public const VkFormat SharedImageFormat = VkFormat.R8G8B8A8Unorm;

    public const VkImageUsageFlags SharedImageUsage =
        VkImageUsageFlags.TransferDst | VkImageUsageFlags.TransferSrc | VkImageUsageFlags.Sampled | VkImageUsageFlags.ColorAttachment;

    /// <summary>Importers create their side of a shared image as mutable-format, and both sides must match.</summary>
    public const VkImageCreateFlags SharedImageFlags = VkImageCreateFlags.MutableFormat;

    private const string ValidationEnvironmentVariable = "TALESMITH_VULKAN_VALIDATION";

    private static readonly VkVersion SharingVersion = VkVersion.Version_1_1;

    private readonly VkDebugUtilsMessengerEXT _messenger;
    private readonly GCHandle _loggerHandle;
    private readonly VkInstanceApi _instanceApi;
    private readonly VkPhysicalDeviceMemoryProperties _memory;

    private VulkanDevice(VkInstanceApi instanceApi, VkDebugUtilsMessengerEXT messenger, GCHandle loggerHandle, SelectedDevice selected,
        VkDeviceApi api, VkQueue queue)
    {
        _instanceApi = instanceApi;
        _messenger = messenger;
        _loggerHandle = loggerHandle;
        QueueFamily = selected.QueueFamily;
        Api = api;
        Queue = queue;
        DeviceUuid = selected.Uuid;
        SupportsSharedImages = selected.Sharing.Supported;
        SharedImagesUnsupportedReason = selected.Sharing.Reason;
        SharedImagesRequireDedicatedAllocation = selected.Sharing.RequiresDedicatedAllocation;

        var physicalDevice = selected.Device;
        VkPhysicalDeviceProperties properties;
        instanceApi.vkGetPhysicalDeviceProperties(physicalDevice, &properties);
        Name = Marshal.PtrToStringUTF8((nint)properties.deviceName) ?? "Vulkan device";
        Limits = properties.limits;
        VkPhysicalDeviceMemoryProperties memory;
        instanceApi.vkGetPhysicalDeviceMemoryProperties(physicalDevice, &memory);
        _memory = memory;

        uint familyCount = 0;
        instanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, null);
        var families = stackalloc VkQueueFamilyProperties[(int)familyCount];
        instanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, families);
        // Lavapipe waits for the whole device to idle when query results are read, which would serialize CPU and GPU.
        SupportsTimestamps = families[QueueFamily].timestampValidBits > 0 && Limits.timestampPeriod > 0 && properties.deviceType != VkPhysicalDeviceType.Cpu;
    }

    public VkDeviceApi Api { get; }

    public VkQueue Queue { get; }

    public uint QueueFamily { get; }

    public string Name { get; }

    public VkPhysicalDeviceLimits Limits { get; }

    /// <summary>Whether GPU time can be measured without stalling the CPU.</summary>
    public bool SupportsTimestamps { get; }

    /// <summary>The device's UUID, which other graphics APIs report for the same GPU; null when the driver does not expose it.</summary>
    public byte[]? DeviceUuid { get; }

    /// <summary>Whether images and semaphores can be exported as POSIX file descriptors for another graphics API.</summary>
    public bool SupportsSharedImages { get; }

    /// <summary>Why <see cref="SupportsSharedImages"/> is false.</summary>
    public string? SharedImagesUnsupportedReason { get; }

    /// <summary>Whether shared images must have their own memory allocation, which the importer must then also use.</summary>
    public bool SharedImagesRequireDedicatedAllocation { get; }

    public static VulkanDevice Create(VulkanRendererOptions options, ILogger logger)
    {
        if (vkInitialize() != VkResult.Success)
            throw new VulkanUnavailableException("The Vulkan loader (libvulkan) could not be loaded.");

        var validation = (options.EnableValidation || Environment.GetEnvironmentVariable(ValidationEnvironmentVariable) == "1")
            && ValidationLayerAvailable(logger);
        var apiVersion = vkEnumerateInstanceVersion() >= SharingVersion ? SharingVersion : VkVersion.Version_1_0;
        var instanceApi = CreateInstance(validation, apiVersion);
        var loggerHandle = default(GCHandle);
        var messenger = VkDebugUtilsMessengerEXT.Null;
        try
        {
            if (validation)
            {
                loggerHandle = GCHandle.Alloc(logger);
                messenger = CreateMessenger(instanceApi, loggerHandle);
            }

            var selected = SelectDevice(instanceApi, options, apiVersion);
            var api = CreateLogicalDevice(instanceApi, selected, out var queue);
            return new VulkanDevice(instanceApi, messenger, loggerHandle, selected, api, queue);
        }
        catch
        {
            if (messenger != VkDebugUtilsMessengerEXT.Null)
                instanceApi.vkDestroyDebugUtilsMessengerEXT(messenger);
            if (loggerHandle.IsAllocated)
                loggerHandle.Free();
            instanceApi.vkDestroyInstance();
            throw;
        }
    }

    /// <summary>Finds a memory type with all <paramref name="required"/> flags, preferring one that also has <paramref name="preferred"/>.</summary>
    public uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags required, VkMemoryPropertyFlags preferred, out VkMemoryPropertyFlags flags)
    {
        var fallback = -1;
        for (var i = 0; i < _memory.memoryTypeCount; i++)
        {
            if ((typeBits & (1u << i)) == 0)
                continue;
            var typeFlags = _memory.memoryTypes[i].propertyFlags;
            if ((typeFlags & required) != required)
                continue;
            if ((typeFlags & preferred) == preferred)
            {
                flags = typeFlags;
                return (uint)i;
            }

            if (fallback < 0)
                fallback = i;
        }

        if (fallback < 0)
            throw new VkException(VkResult.ErrorOutOfDeviceMemory, $"No memory type with {required}.");
        flags = _memory.memoryTypes[fallback].propertyFlags;
        return (uint)fallback;
    }

    public void WaitIdle() => Api.vkDeviceWaitIdle().CheckResult();

    public void Dispose()
    {
        Api.vkDestroyDevice();
        if (_messenger != VkDebugUtilsMessengerEXT.Null)
            _instanceApi.vkDestroyDebugUtilsMessengerEXT(_messenger);
        _instanceApi.vkDestroyInstance();
        if (_loggerHandle.IsAllocated)
            _loggerHandle.Free();
    }

    private static bool ValidationLayerAvailable(ILogger logger)
    {
        uint count = 0;
        vkEnumerateInstanceLayerProperties(&count, null).CheckResult();
        var layers = new VkLayerProperties[count];
        fixed (VkLayerProperties* pointer = layers)
            vkEnumerateInstanceLayerProperties(&count, pointer).CheckResult();
        foreach (var layer in layers)
        {
            var name = new ReadOnlySpan<byte>(layer.layerName, (int)VK_MAX_EXTENSION_NAME_SIZE);
            if (name.StartsWith("VK_LAYER_KHRONOS_validation\0"u8))
                return true;
        }

        LogValidationLayerMissing(logger);
        return false;
    }

    private static VkInstanceApi CreateInstance(bool validation, VkVersion apiVersion)
    {
        var applicationInfo = new VkApplicationInfo
        {
            pApplicationName = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference("Talesmith"u8)),
            pEngineName = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference("Talesmith"u8)),
            apiVersion = apiVersion
        };
        var layer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference("VK_LAYER_KHRONOS_validation"u8));
        var extension = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(VK_EXT_DEBUG_UTILS_EXTENSION_NAME));
        var createInfo = new VkInstanceCreateInfo
        {
            pApplicationInfo = &applicationInfo,
            enabledLayerCount = validation ? 1u : 0u,
            ppEnabledLayerNames = validation ? &layer : null,
            enabledExtensionCount = validation ? 1u : 0u,
            ppEnabledExtensionNames = validation ? &extension : null
        };
        VkInstance instance;
        var result = vkCreateInstance(&createInfo, &instance);
        if (result != VkResult.Success)
            throw new VulkanUnavailableException($"Creating a Vulkan instance failed: {result}.");
        return GetApi(instance);
    }

    private static VkDebugUtilsMessengerEXT CreateMessenger(VkInstanceApi api, GCHandle loggerHandle)
    {
        var createInfo = new VkDebugUtilsMessengerCreateInfoEXT
        {
            messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Warning | VkDebugUtilsMessageSeverityFlagsEXT.Error,
            messageType = VkDebugUtilsMessageTypeFlagsEXT.General | VkDebugUtilsMessageTypeFlagsEXT.Validation | VkDebugUtilsMessageTypeFlagsEXT.Performance,
            pfnUserCallback = &OnDebugMessage,
            pUserData = (void*)GCHandle.ToIntPtr(loggerHandle)
        };
        VkDebugUtilsMessengerEXT messenger;
        api.vkCreateDebugUtilsMessengerEXT(&createInfo, &messenger).CheckResult();
        return messenger;
    }

    [UnmanagedCallersOnly]
    private static uint OnDebugMessage(VkDebugUtilsMessageSeverityFlagsEXT severity, VkDebugUtilsMessageTypeFlagsEXT types,
        VkDebugUtilsMessengerCallbackDataEXT* data, void* userData)
    {
        if (GCHandle.FromIntPtr((nint)userData).Target is ILogger logger)
        {
            var message = Marshal.PtrToStringUTF8((nint)data->pMessage) ?? string.Empty;
            if ((severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) != 0)
                LogValidationError(logger, types, message);
            else
                LogValidationWarning(logger, types, message);
        }

        return VK_FALSE;
    }

    private static SelectedDevice SelectDevice(VkInstanceApi api, VulkanRendererOptions options, VkVersion instanceVersion)
    {
        uint count = 0;
        api.vkEnumeratePhysicalDevices(&count, null).CheckResult();
        var devices = new VkPhysicalDevice[count];
        fixed (VkPhysicalDevice* pointer = devices)
            api.vkEnumeratePhysicalDevices(&count, pointer).CheckResult();

        SelectedDevice? best = null;
        var bestScore = -1;
        var seen = new List<string>();
        foreach (var device in devices)
        {
            VkPhysicalDeviceProperties properties;
            api.vkGetPhysicalDeviceProperties(device, &properties);
            var name = Marshal.PtrToStringUTF8((nint)properties.deviceName) ?? string.Empty;
            seen.Add($"{name} ({properties.deviceType})");
            if (options.DeviceName is { } filter && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (options.UseCpuDevice && properties.deviceType != VkPhysicalDeviceType.Cpu)
                continue;
            if (FindGraphicsQueueFamily(api, device) is not { } family)
                continue;

            var supports11 = instanceVersion >= SharingVersion && new VkVersion(properties.apiVersion) >= SharingVersion;
            var uuid = supports11 ? ReadDeviceUuid(api, device) : null;
            var score = properties.deviceType switch
            {
                VkPhysicalDeviceType.DiscreteGpu => 4,
                VkPhysicalDeviceType.IntegratedGpu => 3,
                VkPhysicalDeviceType.VirtualGpu => 2,
                VkPhysicalDeviceType.Cpu => 1,
                _ => 0
            };
            if (options.DeviceUuid is { } wanted && uuid is not null && wanted.AsSpan().SequenceEqual(uuid))
                score += 100;
            if (score > bestScore)
            {
                bestScore = score;
                var sharing = supports11 ? CheckSharing(api, device) : SharingSupport.No("the device does not support Vulkan 1.1");
                best = new SelectedDevice(device, family, uuid, sharing);
            }
        }

        if (best is null)
        {
            var available = seen.Count == 0 ? "none" : string.Join(", ", seen);
            throw new VulkanUnavailableException($"No Vulkan device matches the options. Available devices: {available}.");
        }

        return best;
    }

    private static byte[] ReadDeviceUuid(VkInstanceApi api, VkPhysicalDevice device)
    {
        var id = new VkPhysicalDeviceIDProperties();
        var properties = new VkPhysicalDeviceProperties2 { pNext = &id };
        api.vkGetPhysicalDeviceProperties2(device, &properties);
        return new ReadOnlySpan<byte>(id.deviceUUID, (int)VK_UUID_SIZE).ToArray();
    }

    private static SharingSupport CheckSharing(VkInstanceApi api, VkPhysicalDevice device)
    {
        if (!HasExtension(api, device, VK_KHR_EXTERNAL_MEMORY_FD_EXTENSION_NAME) || !HasExtension(api, device, VK_KHR_EXTERNAL_SEMAPHORE_FD_EXTENSION_NAME))
            return SharingSupport.No("the driver does not export memory and semaphores as file descriptors");

        var external = new VkPhysicalDeviceExternalImageFormatInfo { handleType = VkExternalMemoryHandleTypeFlags.OpaqueFD };
        var formatInfo = new VkPhysicalDeviceImageFormatInfo2
        {
            pNext = &external,
            format = SharedImageFormat,
            type = VkImageType.Image2D,
            tiling = VkImageTiling.Optimal,
            usage = SharedImageUsage,
            flags = SharedImageFlags
        };
        var externalProperties = new VkExternalImageFormatProperties();
        var formatProperties = new VkImageFormatProperties2 { pNext = &externalProperties };
        if (api.vkGetPhysicalDeviceImageFormatProperties2(device, &formatInfo, &formatProperties) != VkResult.Success)
            return SharingSupport.No("RGBA8 images cannot be exported");
        var features = externalProperties.externalMemoryProperties.externalMemoryFeatures;
        if ((features & VkExternalMemoryFeatureFlags.Exportable) == 0)
            return SharingSupport.No("RGBA8 images cannot be exported");

        var semaphoreInfo = new VkPhysicalDeviceExternalSemaphoreInfo { handleType = VkExternalSemaphoreHandleTypeFlags.OpaqueFD };
        var semaphoreProperties = new VkExternalSemaphoreProperties();
        api.vkGetPhysicalDeviceExternalSemaphoreProperties(device, &semaphoreInfo, &semaphoreProperties);
        return (semaphoreProperties.externalSemaphoreFeatures & VkExternalSemaphoreFeatureFlags.Exportable) == 0
            ? SharingSupport.No("semaphores cannot be exported")
            : SharingSupport.Yes((features & VkExternalMemoryFeatureFlags.DedicatedOnly) != 0);
    }

    private static bool HasExtension(VkInstanceApi api, VkPhysicalDevice device, ReadOnlySpan<byte> name)
    {
        name = name.TrimEnd((byte)0);
        uint count = 0;
        api.vkEnumerateDeviceExtensionProperties(device, null, &count, null).CheckResult();
        var extensions = new VkExtensionProperties[count];
        fixed (VkExtensionProperties* pointer = extensions)
            api.vkEnumerateDeviceExtensionProperties(device, null, &count, pointer).CheckResult();
        foreach (var extension in extensions)
        {
            var extensionName = new ReadOnlySpan<byte>(extension.extensionName, (int)VK_MAX_EXTENSION_NAME_SIZE);
            var length = extensionName.IndexOf((byte)0);
            if (extensionName[..(length < 0 ? extensionName.Length : length)].SequenceEqual(name))
                return true;
        }

        return false;
    }

    private static uint? FindGraphicsQueueFamily(VkInstanceApi api, VkPhysicalDevice device)
    {
        uint count = 0;
        api.vkGetPhysicalDeviceQueueFamilyProperties(device, &count, null);
        var families = stackalloc VkQueueFamilyProperties[(int)count];
        api.vkGetPhysicalDeviceQueueFamilyProperties(device, &count, families);
        for (var i = 0u; i < count; i++)
        {
            if ((families[i].queueFlags & VkQueueFlags.Graphics) != 0)
                return i;
        }

        return null;
    }

    private static VkDeviceApi CreateLogicalDevice(VkInstanceApi api, SelectedDevice selected, out VkQueue queue)
    {
        var priority = 1f;
        var queueInfo = new VkDeviceQueueCreateInfo
        {
            queueFamilyIndex = selected.QueueFamily,
            queueCount = 1,
            pQueuePriorities = &priority
        };
        var extensions = stackalloc byte*[2];
        extensions[0] = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(VK_KHR_EXTERNAL_MEMORY_FD_EXTENSION_NAME));
        extensions[1] = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(VK_KHR_EXTERNAL_SEMAPHORE_FD_EXTENSION_NAME));
        var createInfo = new VkDeviceCreateInfo
        {
            queueCreateInfoCount = 1,
            pQueueCreateInfos = &queueInfo,
            enabledExtensionCount = selected.Sharing.Supported ? 2u : 0u,
            ppEnabledExtensionNames = selected.Sharing.Supported ? extensions : null
        };
        VkDevice device;
        var result = api.vkCreateDevice(selected.Device, &createInfo, &device);
        if (result != VkResult.Success)
            throw new VulkanUnavailableException($"Creating the Vulkan device failed: {result}.");
        var deviceApi = GetApi(api.Instance, device);
        VkQueue graphicsQueue;
        deviceApi.vkGetDeviceQueue(selected.QueueFamily, 0, &graphicsQueue);
        queue = graphicsQueue;
        return deviceApi;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vulkan validation was requested but VK_LAYER_KHRONOS_validation is not installed.")]
    private static partial void LogValidationLayerMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vulkan {Types}: {Message}")]
    private static partial void LogValidationError(ILogger logger, VkDebugUtilsMessageTypeFlagsEXT types, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Vulkan {Types}: {Message}")]
    private static partial void LogValidationWarning(ILogger logger, VkDebugUtilsMessageTypeFlagsEXT types, string message);

    private sealed record SelectedDevice(VkPhysicalDevice Device, uint QueueFamily, byte[]? Uuid, SharingSupport Sharing);

    private readonly record struct SharingSupport(bool Supported, bool RequiresDedicatedAllocation, string? Reason)
    {
        public static SharingSupport Yes(bool requiresDedicatedAllocation) => new(true, requiresDedicatedAllocation, null);

        public static SharingSupport No(string reason) => new(false, false, reason);
    }
}
