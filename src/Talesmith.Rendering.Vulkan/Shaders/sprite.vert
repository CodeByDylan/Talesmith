#version 450

// Per-instance SpriteInstance: the transform's rows, the source rectangle in texels and a straight-alpha tint.
layout(location = 0) in vec2 transformX;
layout(location = 1) in vec2 transformY;
layout(location = 2) in vec2 transformTranslation;
layout(location = 3) in vec4 source;
layout(location = 4) in vec4 tint;

layout(push_constant) uniform Constants
{
    vec4 viewLinear;
    vec2 viewTranslation;
    vec2 targetSize;
    vec2 inverseTextureSize;
} constants;

layout(location = 0) out vec2 uv;
layout(location = 1) out vec4 premultipliedTint;

void main()
{
    vec2 corner = vec2(gl_VertexIndex & 1, gl_VertexIndex >> 1);
    vec2 world = transformX * corner.x + transformY * corner.y + transformTranslation;
    vec2 pixel = constants.viewLinear.xy * world.x + constants.viewLinear.zw * world.y + constants.viewTranslation;
    gl_Position = vec4(pixel / constants.targetSize * 2.0 - 1.0, 0.0, 1.0);
    uv = (source.xy + corner * source.zw) * constants.inverseTextureSize;
    premultipliedTint = vec4(tint.rgb * tint.a, tint.a);
}
