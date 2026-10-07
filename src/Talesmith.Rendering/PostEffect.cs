using System.Numerics;

namespace Talesmith.Rendering;

/// <summary>A full-screen shader applied to the rendered frame, such as a fade, vignette or color grade.</summary>
/// <remarks>Parameters may change every frame; the values at the time the effect is added to a frame are used.</remarks>
public sealed class PostEffect(ShaderSource shader)
{
    private readonly Vector4[] _parameters = new Vector4[4];

    public ShaderSource Shader { get; } = shader;

    public bool Enabled { get; set; } = true;

    public Span<Vector4> Parameters => _parameters;
}

/// <summary>A post effect with its parameters as they were when the frame was built.</summary>
public readonly record struct PostEffectInstance(ShaderSource Shader, Vector4 P0, Vector4 P1, Vector4 P2, Vector4 P3);
