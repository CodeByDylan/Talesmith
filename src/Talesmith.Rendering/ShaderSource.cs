namespace Talesmith.Rendering;

/// <summary>A custom fragment shader with one source per backend, used by <see cref="Material"/>s and <see cref="PostEffect"/>s.</summary>
/// <remarks>
/// <para>A backend without a source for itself draws as if no shader were set. Every shader can read four <c>float4</c> parameters.</para>
/// <para><b>Skia (SkSL).</b> Material shaders declare <c>uniform shader image; uniform float4 params[4];</c> and implement
/// <c>half4 main(float2 coord)</c>, where <c>coord</c> is in texture pixels; sample with <c>image.eval(coord)</c>. The sprite tint is
/// multiplied in afterwards. Post effect shaders declare <c>uniform shader scene; uniform float2 resolution; uniform float time;
/// uniform float4 params[4];</c> and receive <c>coord</c> in pixels of the view, with <c>resolution</c> its size; the bars around the
/// view are left alone.</para>
/// <para><b>Vulkan (SPIR-V from GLSL 450).</b> Material shaders read <c>layout(location = 0) in vec2 uv</c> and
/// <c>layout(location = 1) in vec4 tint</c> (premultiplied), sample <c>layout(set = 0, binding = 0) uniform sampler2D image</c> and write
/// <c>layout(location = 0) out vec4 color</c> with premultiplied alpha. Post effect shaders read <c>uv</c> at location 0, from 0 to 1
/// across the view, and sample <c>scene</c> at set 0, binding 0. Both can read <c>layout(set = 1, binding = 0) uniform Effect { vec4 params[4]; vec2 resolution;
/// float time; }</c> (std140).</para>
/// </remarks>
public sealed class ShaderSource
{
    public ShaderSource(string name, string? skSl = null, byte[]? fragmentSpirV = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        SkSl = skSl;
        FragmentSpirV = fragmentSpirV;
    }

    public string Name { get; }

    public string? SkSl { get; }

    public byte[]? FragmentSpirV { get; }

    public override string ToString() => Name;
}
