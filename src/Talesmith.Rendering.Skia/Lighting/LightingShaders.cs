namespace Talesmith.Rendering.Skia.Lighting;

/// <summary>The SkSL used to draw light maps; the Vulkan backend's light.frag implements the same shading.</summary>
internal static class LightingShaders
{
    /// <summary>
    /// Shades one light. Local coordinates are the offset from the light in world units; uniforms follow
    /// <see cref="Rendering.Lighting.LightShaderData"/>, then the cookie size and the shadow map size in pixels. The occluders child is
    /// the <see cref="Rendering.Lighting.OccluderMaskBuilder"/> mask, placed in the same local coordinates.
    /// </summary>
    public const string Light = """
        uniform shader cookie;
        uniform shader shadowMap;
        uniform shader occluders;
        uniform float4 color;
        uniform float4 shape;
        uniform float4 cone;
        uniform float4 shadow;
        uniform float4 extra;
        uniform float2 cookieSize;
        uniform float2 mapSize;

        const float TAU = 6.28318530718;
        const int MAX_SAMPLES = 16;

        float depthAt(float s, bool wrapped) {
            float x = wrapped ? fract(s) : clamp(s, 0.0, 0.99999);
            return shadowMap.eval(float2(floor(x * mapSize.x) + 0.5, shadow.x * mapSize.y)).r;
        }

        float blocks(float stored, float depth) {
            return (stored < depth - (0.002 + depth * 0.006) && (extra.y <= 0.0 || depth - stored < extra.y)) ? 1.0 : 0.0;
        }

        float occlusion(float s, float depth, bool wrapped, float jitter) {
            float samples = shadow.w;
            if (samples < 1.5) {
                return blocks(depthAt(s, wrapped), depth);
            }

            float texel = 1.0 / extra.x;
            float source = shadow.z * 0.15;
            float search = wrapped
                ? min(4.0 * source / max(depth, 0.1), 1.2) / TAU + 2.0 * texel
                : shadow.z * 0.12 + 2.0 * texel;

            float blockerSum = 0.0;
            float blockerCount = 0.0;
            for (int i = 0; i < MAX_SAMPLES; i++) {
                if (float(i) >= samples) {
                    break;
                }
                float stored = depthAt(s + ((float(i) + jitter) / samples - 0.5) * search, wrapped);
                float blocked = blocks(stored, depth);
                blockerSum += stored * blocked;
                blockerCount += blocked;
            }
            if (blockerCount == 0.0) {
                return 0.0;
            }

            float blocker = blockerSum / blockerCount;
            float penumbra = wrapped
                ? source * (depth - blocker) / (max(blocker, 0.02) * depth) / TAU
                : shadow.z * 0.4 * (depth - blocker);
            penumbra = clamp(penumbra, 0.0, search);

            float shadowed = 0.0;
            for (int i = 0; i < MAX_SAMPLES; i++) {
                if (float(i) >= samples) {
                    break;
                }
                shadowed += blocks(depthAt(s + ((float(i) + jitter) / samples - 0.5) * penumbra, wrapped), depth);
            }
            return shadowed / samples;
        }

        half4 main(float2 p) {
            float radius = shape.x;
            bool directional = shape.w > 1.5;
            float distance = length(p);
            float a = 1.0;
            if (!directional) {
                a = pow(clamp((1.0 - distance / radius) / (1.0 - shape.y), 0.0, 1.0), shape.z);
                a *= smoothstep(cone.z, cone.w, dot(p / max(distance, 0.0001), cone.xy));
            }

            // Shadows fall outside the occluders that do not shadow themselves; inside them, the light is unshadowed.
            float open = a > 0.0 && shadow.x >= 0.0 ? 1.0 - float(occluders.eval(p).a) : 0.0;
            if (open > 0.0) {
                float s;
                float depth;
                if (directional) {
                    s = dot(p, float2(-cone.y, cone.x)) / (2.0 * radius) + 0.5;
                    depth = dot(p, cone.xy) / (2.0 * radius) + 0.5;
                } else {
                    s = atan(p.y, p.x) / TAU + 0.5;
                    depth = distance / radius;
                }
                float jitter = fract(52.9829189 * fract(dot(floor(p * extra.w), float2(0.06711056, 0.00583715))));
                a *= 1.0 - shadow.y * open * occlusion(s, depth, !directional, jitter);
            }

            float3 k = float3(a);
            if (extra.z > 0.5) {
                float2 local = float2(dot(p, cone.xy), dot(p, float2(-cone.y, cone.x)));
                k *= cookie.eval((local / (2.0 * radius) + 0.5) * cookieSize).rgb;
            }

            if (color.w < 0.5) {
                return half4(half3(color.rgb * k), 0.0);
            }
            if (color.w < 1.5) {
                return half4(half3(color.rgb * k), half(max(k.r, max(k.g, k.b))));
            }
            return half4(half3(mix(float3(1.0), color.rgb, k)), 1.0);
        }
        """;

    /// <summary>Multiplies the scene by the light map, which stores half the light so it can brighten up to twice.</summary>
    public const string Composite = """
        half4 main(half4 src, half4 dst) {
            return half4(dst.rgb * src.rgb * 2.0, dst.a);
        }
        """;
}
