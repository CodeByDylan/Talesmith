using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Talesmith.Rendering.Skia;

/// <summary>A compiled SkSL effect that makes shaders with one child shader and the engine's standard uniforms.</summary>
internal sealed class RuntimeShader : IDisposable
{
    private const int FloatType = 0;
    private const int Float2Type = 1;
    private const int Float4Type = 3;

    private readonly SKRuntimeEffect _effect;
    private readonly byte[] _uniforms;
    private readonly nint[] _children;
    private readonly int _childIndex;
    private readonly int _paramsOffset;
    private readonly int _resolutionOffset;
    private readonly int _timeOffset;

    private RuntimeShader(SKRuntimeEffect effect, int childIndex, int paramsOffset, int resolutionOffset, int timeOffset)
    {
        _effect = effect;
        _uniforms = new byte[Math.Max(1, effect.UniformSize)];
        _children = new nint[Math.Max(1, effect.Children.Count)];
        _childIndex = childIndex;
        _paramsOffset = paramsOffset;
        _resolutionOffset = resolutionOffset;
        _timeOffset = timeOffset;
    }

    /// <summary>Compiles SkSL and checks it only declares <paramref name="childName"/> and the allowed uniforms.</summary>
    public static RuntimeShader? Compile(string skSl, string childName, bool allowFrameUniforms, out string? error)
    {
        var effect = SKRuntimeEffect.CreateShader(skSl, out error);
        if (effect is null)
            return null;

        var children = effect.Children;
        if (children.Count > 1 || (children.Count == 1 && children[0] != childName))
        {
            error = $"Only one child shader, named '{childName}', is supported.";
            effect.Dispose();
            return null;
        }

        int paramsOffset = -1, resolutionOffset = -1, timeOffset = -1;
        var names = effect.Uniforms;
        for (var i = 0; i < names.Count; i++)
        {
            SkiaNative.sk_runtimeeffect_get_uniform_from_index(effect.Handle, i, out var uniform);
            var offset = (int)uniform.Offset;
            switch (names[i])
            {
                case "params" when uniform is { Type: Float4Type, Count: 4 }:
                    paramsOffset = offset;
                    break;
                case "resolution" when allowFrameUniforms && uniform is { Type: Float2Type, Count: <= 1 }:
                    resolutionOffset = offset;
                    break;
                case "time" when allowFrameUniforms && uniform is { Type: FloatType, Count: <= 1 }:
                    timeOffset = offset;
                    break;
                default:
                    error = $"Uniform '{names[i]}' is not supported here or has the wrong type; see ShaderSource for the uniforms a shader may declare.";
                    effect.Dispose();
                    return null;
            }
        }

        error = null;
        return new RuntimeShader(effect, children.Count == 1 ? 0 : -1, paramsOffset, resolutionOffset, timeOffset);
    }

    /// <summary>Creates a native shader handle that the caller must release with <see cref="SkiaNative.sk_shader_unref"/>.</summary>
    public nint CreateShader(nint child, ReadOnlySpan<Vector4> parameters, Vector2 resolution = default, float time = 0)
    {
        if (_paramsOffset >= 0)
            MemoryMarshal.AsBytes(parameters).CopyTo(_uniforms.AsSpan(_paramsOffset));
        if (_resolutionOffset >= 0)
            MemoryMarshal.Write(_uniforms.AsSpan(_resolutionOffset), resolution);
        if (_timeOffset >= 0)
            MemoryMarshal.Write(_uniforms.AsSpan(_timeOffset), time);
        if (_childIndex >= 0)
            _children[_childIndex] = child;

        var data = SkiaNative.sk_data_new_with_copy(in _uniforms[0], _effect.UniformSize);
        var shader = SkiaNative.sk_runtimeeffect_make_shader(_effect.Handle, data, in _children[0], _effect.Children.Count, 0);
        SkiaNative.sk_data_unref(data);
        return shader;
    }

    public void Dispose() => _effect.Dispose();
}
