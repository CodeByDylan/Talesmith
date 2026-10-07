using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Talesmith.Rendering.Skia;

/// <summary>Compiles each <see cref="ShaderSource"/> once for one use (materials or post effects) and remembers failures.</summary>
internal sealed partial class ShaderLibrary(string childName, bool allowFrameUniforms, ILogger? logger) : IDisposable
{
    private readonly Dictionary<ShaderSource, RuntimeShader?> _shaders = [];

    /// <summary>Gets the compiled shader, or null when the source has no SkSL or does not compile.</summary>
    public RuntimeShader? Get(ShaderSource source)
    {
        if (_shaders.TryGetValue(source, out var shader))
            return shader;

        if (source.SkSl is not null)
        {
            shader = RuntimeShader.Compile(source.SkSl, childName, allowFrameUniforms, out var error);
            if (shader is null)
                ReportCompileFailure(source.Name, error ?? "unknown error");
        }

        _shaders.Add(source, shader);
        return shader;
    }

    public void Dispose()
    {
        foreach (var shader in _shaders.Values)
            shader?.Dispose();
        _shaders.Clear();
    }

    private void ReportCompileFailure(string shader, string error)
    {
        if (logger is null)
            Trace.TraceError($"Shader '{shader}' failed to compile and is drawn without it: {error}");
        else
            LogCompileFailed(logger, shader, error);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Shader '{Shader}' failed to compile and is drawn without it: {Error}")]
    private static partial void LogCompileFailed(ILogger logger, string shader, string error);
}
