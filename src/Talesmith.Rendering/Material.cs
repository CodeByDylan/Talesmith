using System.Numerics;

namespace Talesmith.Rendering;

/// <summary>How sprites are drawn: their blend mode and, optionally, a custom shader with parameters.</summary>
/// <remarks>Materials are immutable and compared by reference, so create them once and reuse them; draws that share a texture and a material are batched.</remarks>
public sealed class Material
{
    private readonly Vector4[] _parameters;

    public Material(BlendMode blend = BlendMode.Alpha, ShaderSource? shader = null, ReadOnlySpan<Vector4> parameters = default)
    {
        if (parameters.Length > 4)
            throw new ArgumentException("A material has at most four parameters.", nameof(parameters));
        Blend = blend;
        Shader = shader;
        _parameters = new Vector4[4];
        parameters.CopyTo(_parameters);
    }

    /// <summary>Plain sprites with normal transparency.</summary>
    public static Material Default { get; } = new();

    public static Material Additive { get; } = new(BlendMode.Additive);

    public static Material Multiply { get; } = new(BlendMode.Multiply);

    public BlendMode Blend { get; }

    public ShaderSource? Shader { get; }

    /// <summary>The four values passed to the shader as <c>params</c>.</summary>
    public ReadOnlySpan<Vector4> Parameters => _parameters;
}
