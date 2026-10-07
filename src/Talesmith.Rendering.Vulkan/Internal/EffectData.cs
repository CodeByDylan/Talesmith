using System.Numerics;
using System.Runtime.InteropServices;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>The std140 <c>Effect</c> uniform block custom shaders read at set 1, binding 0.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EffectData
{
    public Vector4 P0;
    public Vector4 P1;
    public Vector4 P2;
    public Vector4 P3;
    public Vector2 Resolution;
    public float Time;
    private readonly float _padding;

    public EffectData(ReadOnlySpan<Vector4> parameters, Vector2 resolution, float time)
        : this(parameters[0], parameters[1], parameters[2], parameters[3], resolution, time)
    {
    }

    public EffectData(Vector4 p0, Vector4 p1, Vector4 p2, Vector4 p3, Vector2 resolution, float time)
    {
        P0 = p0;
        P1 = p1;
        P2 = p2;
        P3 = p3;
        Resolution = resolution;
        Time = time;
        _padding = 0;
    }
}
