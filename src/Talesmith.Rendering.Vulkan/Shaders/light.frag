#version 450

// Shades one light into the light map, which stores half the light. Matches the Skia backend's LightingShaders.Light.
layout(location = 0) in vec2 offset;

layout(set = 0, binding = 0) uniform sampler2D cookie;
layout(set = 1, binding = 0) uniform Light
{
    vec4 color;  // rgb, blend: 0 additive, 1 mix, 2 multiply
    vec4 shape;  // radius, inner radius fraction, falloff, type: 0 point, 1 spot, 2 directional
    vec4 cone;   // direction, cos of half the outer and inner angles
    vec4 shadow; // shadow map row coordinate (negative without shadows), strength, softness, samples
    vec4 extra;  // shadow map width, normalized directional shadow length, has cookie, light map pixels per world unit
} light;
layout(set = 2, binding = 0) uniform sampler2D shadowMap;
// Coverage of the occluders that do not shadow themselves, in light map pixels (see OccluderMaskBuilder).
layout(set = 3, binding = 0) uniform sampler2D occluders;

layout(location = 0) out vec4 result;

const float TAU = 6.28318530718;
const int MAX_SAMPLES = 16;

float depthAt(float s, bool wrapped)
{
    float x = wrapped ? fract(s) : clamp(s, 0.0, 0.99999);
    return texture(shadowMap, vec2((floor(x * light.extra.x) + 0.5) / light.extra.x, light.shadow.x)).r;
}

float blocks(float stored, float depth)
{
    return (stored < depth - (0.002 + depth * 0.006) && (light.extra.y <= 0.0 || depth - stored < light.extra.y)) ? 1.0 : 0.0;
}

float occlusion(float s, float depth, bool wrapped, float jitter)
{
    float samples = light.shadow.w;
    if (samples < 1.5)
        return blocks(depthAt(s, wrapped), depth);

    float texel = 1.0 / light.extra.x;
    float source = light.shadow.z * 0.15;
    float search = wrapped
        ? min(4.0 * source / max(depth, 0.1), 1.2) / TAU + 2.0 * texel
        : light.shadow.z * 0.12 + 2.0 * texel;

    float blockerSum = 0.0;
    float blockerCount = 0.0;
    for (int i = 0; i < MAX_SAMPLES; i++)
    {
        if (float(i) >= samples)
            break;
        float stored = depthAt(s + ((float(i) + jitter) / samples - 0.5) * search, wrapped);
        float blocked = blocks(stored, depth);
        blockerSum += stored * blocked;
        blockerCount += blocked;
    }
    if (blockerCount == 0.0)
        return 0.0;

    float blocker = blockerSum / blockerCount;
    float penumbra = wrapped
        ? source * (depth - blocker) / (max(blocker, 0.02) * depth) / TAU
        : light.shadow.z * 0.4 * (depth - blocker);
    penumbra = clamp(penumbra, 0.0, search);

    float shadowed = 0.0;
    for (int i = 0; i < MAX_SAMPLES; i++)
    {
        if (float(i) >= samples)
            break;
        shadowed += blocks(depthAt(s + ((float(i) + jitter) / samples - 0.5) * penumbra, wrapped), depth);
    }
    return shadowed / samples;
}

void main()
{
    vec2 p = offset;
    float radius = light.shape.x;
    bool directional = light.shape.w > 1.5;
    float distance = length(p);
    float a = 1.0;
    if (!directional)
    {
        a = pow(clamp((1.0 - distance / radius) / (1.0 - light.shape.y), 0.0, 1.0), light.shape.z);
        a *= smoothstep(light.cone.z, light.cone.w, dot(p / max(distance, 0.0001), light.cone.xy));
    }

    // Shadows fall outside the occluders that do not shadow themselves; inside them, the light is unshadowed.
    float open = a > 0.0 && light.shadow.x >= 0.0 ? 1.0 - texelFetch(occluders, ivec2(gl_FragCoord.xy), 0).r : 0.0;
    if (open > 0.0)
    {
        float s;
        float depth;
        if (directional)
        {
            s = dot(p, vec2(-light.cone.y, light.cone.x)) / (2.0 * radius) + 0.5;
            depth = dot(p, light.cone.xy) / (2.0 * radius) + 0.5;
        }
        else
        {
            s = atan(p.y, p.x) / TAU + 0.5;
            depth = distance / radius;
        }
        float jitter = fract(52.9829189 * fract(dot(floor(p * light.extra.w), vec2(0.06711056, 0.00583715))));
        a *= 1.0 - light.shadow.y * open * occlusion(s, depth, !directional, jitter);
    }

    vec3 k = vec3(a);
    if (light.extra.z > 0.5)
    {
        vec2 local = vec2(dot(p, light.cone.xy), dot(p, vec2(-light.cone.y, light.cone.x)));
        k *= texture(cookie, local / (2.0 * radius) + 0.5).rgb;
    }

    if (light.color.w < 0.5)
        result = vec4(light.color.rgb * k, 0.0);
    else if (light.color.w < 1.5)
        result = vec4(light.color.rgb * k, max(k.r, max(k.g, k.b)));
    else
        result = vec4(mix(vec3(1.0), light.color.rgb, k), 1.0);
}
