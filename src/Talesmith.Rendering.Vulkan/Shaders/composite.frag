#version 450

// Outputs the light map, which the composite pipeline's blend state multiplies into the scene twice over.
layout(location = 0) in vec2 uv;

layout(set = 0, binding = 0) uniform sampler2D lightMap;

layout(location = 0) out vec4 color;

void main()
{
    color = vec4(texture(lightMap, uv).rgb, 1.0);
}
