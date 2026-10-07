using Microsoft.Extensions.Logging;
using Vortice.Vulkan;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Shader modules: the built-in sprite, full-screen and lighting shaders, and custom fragment shaders loaded on first use.</summary>
internal sealed unsafe partial class ShaderLibrary : IDisposable
{
    private readonly VulkanDevice _device;
    private readonly ILogger _logger;
    private readonly Dictionary<ShaderSource, VkShaderModule> _custom = [];

    public ShaderLibrary(VulkanDevice device, ILogger logger)
    {
        _device = device;
        _logger = logger;
        SpriteVertex = CreateModule(LoadBuiltIn("sprite.vert.spv"));
        SpriteFragment = CreateModule(LoadBuiltIn("sprite.frag.spv"));
        FullScreenVertex = CreateModule(LoadBuiltIn("fullscreen.vert.spv"));
        LightFragment = CreateModule(LoadBuiltIn("light.frag.spv"));
        EmissiveFragment = CreateModule(LoadBuiltIn("emissive.frag.spv"));
        CompositeFragment = CreateModule(LoadBuiltIn("composite.frag.spv"));
    }

    public VkShaderModule SpriteVertex { get; }

    public VkShaderModule SpriteFragment { get; }

    public VkShaderModule FullScreenVertex { get; }

    /// <summary>Shades one light into the light map.</summary>
    public VkShaderModule LightFragment { get; }

    /// <summary>Adds a glowing sprite's mask to the light map.</summary>
    public VkShaderModule EmissiveFragment { get; }

    /// <summary>Samples the light map for compositing.</summary>
    public VkShaderModule CompositeFragment { get; }

    /// <summary>Gets the module for a custom fragment shader; false when it has no usable SPIR-V, which is logged once.</summary>
    public bool TryGetFragment(ShaderSource shader, out VkShaderModule module)
    {
        if (!_custom.TryGetValue(shader, out module))
        {
            module = LoadCustom(shader);
            _custom[shader] = module;
        }

        return module != VkShaderModule.Null;
    }

    public void Dispose()
    {
        foreach (var module in _custom.Values)
        {
            if (module != VkShaderModule.Null)
                _device.Api.vkDestroyShaderModule(module);
        }

        _custom.Clear();
        _device.Api.vkDestroyShaderModule(SpriteVertex);
        _device.Api.vkDestroyShaderModule(SpriteFragment);
        _device.Api.vkDestroyShaderModule(FullScreenVertex);
        _device.Api.vkDestroyShaderModule(LightFragment);
        _device.Api.vkDestroyShaderModule(EmissiveFragment);
        _device.Api.vkDestroyShaderModule(CompositeFragment);
    }

    private VkShaderModule LoadCustom(ShaderSource shader)
    {
        if (shader.FragmentSpirV is not { } code)
            return VkShaderModule.Null;
        if (SpirVInspector.FindFragmentShaderProblem(code) is { } problem)
        {
            LogShaderRejected(_logger, shader.Name, problem);
            return VkShaderModule.Null;
        }

        try
        {
            return CreateModule(code);
        }
        catch (VkException exception)
        {
            LogShaderRejected(_logger, shader.Name, exception.Message);
            return VkShaderModule.Null;
        }
    }

    private VkShaderModule CreateModule(ReadOnlySpan<byte> code)
    {
        fixed (byte* pointer = code)
        {
            var createInfo = new VkShaderModuleCreateInfo { codeSize = (nuint)code.Length, pCode = (uint*)pointer };
            VkShaderModule module;
            _device.Api.vkCreateShaderModule(&createInfo, &module).CheckResult();
            return module;
        }
    }

    private static byte[] LoadBuiltIn(string fileName)
    {
        var name = $"Talesmith.Rendering.Vulkan.Shaders.{fileName}";
        using var stream = typeof(ShaderLibrary).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The built-in shader {name} is not embedded.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Shader '{Name}' cannot be used by Vulkan and is drawn with the default shader: {Reason}")]
    private static partial void LogShaderRejected(ILogger logger, string name, string reason);
}
