#version 450

// Adds a glowing sprite's light to the light map: its tint where the texture is opaque.
layout(location = 0) in vec2 uv;
layout(location = 1) in vec4 tint;

layout(set = 0, binding = 0) uniform sampler2D image;

layout(location = 0) out vec4 color;

void main()
{
    color = vec4(tint.rgb * texture(image, uv).a, 0.0);
}
