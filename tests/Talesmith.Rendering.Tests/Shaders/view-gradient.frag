#version 450

// Writes the post effect's uv as red and green, so tests can see which part of the target the effect covers.
layout(location = 0) in vec2 uv;

layout(set = 0, binding = 0) uniform sampler2D scene;

layout(location = 0) out vec4 color;

void main()
{
    color = vec4(uv, texture(scene, uv).b, 1.0);
}
