using System.Runtime.InteropServices;
using SkiaSharp;

namespace Talesmith.Rendering.Skia;

/// <summary>Turns sprite instances into native Skia triangle meshes, reusing its buffers between calls.</summary>
/// <remarks>
/// Texture coordinates are in texture pixels and colors are the straight-alpha tints, which Skia premultiplies. Colors are omitted when
/// every tint is white. Buffers live on the pinned object heap so their addresses can be passed to Skia directly.
/// </remarks>
internal sealed class QuadVertices
{
    /// <summary>The most quads whose vertices 16-bit indices can address.</summary>
    private const int MaxIndexedQuads = 65536 / 4;

    private const int InitialCapacity = 4096;

    private static readonly ushort[] QuadIndices = CreateQuadIndices();
    private static readonly nint QuadIndicesAddress = Marshal.UnsafeAddrOfPinnedArrayElement(QuadIndices, 0);

    private SKPoint[] _positions = GC.AllocateUninitializedArray<SKPoint>(InitialCapacity, pinned: true);
    private SKPoint[] _texCoords = GC.AllocateUninitializedArray<SKPoint>(InitialCapacity, pinned: true);
    private SKColor[] _colors = GC.AllocateUninitializedArray<SKColor>(InitialCapacity, pinned: true);

    /// <summary>Creates a native <c>sk_vertices_t</c> for one or more instances; release it with <see cref="SkiaNative.sk_vertices_unref"/>.</summary>
    public nint Create(ReadOnlySpan<SpriteInstance> instances)
    {
        var indexed = instances.Length <= MaxIndexedQuads;
        var vertexCount = instances.Length * (indexed ? 4 : 6);
        EnsureCapacity(vertexCount);
        var tinted = indexed ? WriteIndexed(instances) : WriteUnindexed(instances);
        return SkiaNative.sk_vertices_make_copy(
            SKVertexMode.Triangles,
            vertexCount,
            Marshal.UnsafeAddrOfPinnedArrayElement(_positions, 0),
            Marshal.UnsafeAddrOfPinnedArrayElement(_texCoords, 0),
            tinted ? Marshal.UnsafeAddrOfPinnedArrayElement(_colors, 0) : 0,
            indexed ? instances.Length * 6 : 0,
            indexed ? QuadIndicesAddress : 0);
    }

    /// <summary>Writes four vertices per quad and returns whether any tint is not white.</summary>
    private bool WriteIndexed(ReadOnlySpan<SpriteInstance> instances)
    {
        var positions = _positions;
        var texCoords = _texCoords;
        var colors = _colors;
        var tinted = false;
        var v = 0;
        foreach (ref readonly var instance in instances)
        {
            ref readonly var m = ref instance.Transform;
            ref readonly var source = ref instance.Source;
            float right = source.X + source.Width, bottom = source.Y + source.Height;

            positions[v] = new SKPoint(m.M31, m.M32);
            positions[v + 1] = new SKPoint(m.M11 + m.M31, m.M12 + m.M32);
            positions[v + 2] = new SKPoint(m.M11 + m.M21 + m.M31, m.M12 + m.M22 + m.M32);
            positions[v + 3] = new SKPoint(m.M21 + m.M31, m.M22 + m.M32);
            texCoords[v] = new SKPoint(source.X, source.Y);
            texCoords[v + 1] = new SKPoint(right, source.Y);
            texCoords[v + 2] = new SKPoint(right, bottom);
            texCoords[v + 3] = new SKPoint(source.X, bottom);
            var color = (SKColor)instance.Tint.Argb;
            tinted |= color != SKColors.White;
            colors.AsSpan(v, 4).Fill(color);
            v += 4;
        }

        return tinted;
    }

    /// <summary>Writes two separate triangles per quad and returns whether any tint is not white.</summary>
    private bool WriteUnindexed(ReadOnlySpan<SpriteInstance> instances)
    {
        var positions = _positions;
        var texCoords = _texCoords;
        var colors = _colors;
        var tinted = false;
        var v = 0;
        foreach (ref readonly var instance in instances)
        {
            ref readonly var m = ref instance.Transform;
            ref readonly var source = ref instance.Source;
            float right = source.X + source.Width, bottom = source.Y + source.Height;
            var topLeft = new SKPoint(m.M31, m.M32);
            var bottomRight = new SKPoint(m.M11 + m.M21 + m.M31, m.M12 + m.M22 + m.M32);

            positions[v] = topLeft;
            positions[v + 1] = new SKPoint(m.M11 + m.M31, m.M12 + m.M32);
            positions[v + 2] = bottomRight;
            positions[v + 3] = topLeft;
            positions[v + 4] = bottomRight;
            positions[v + 5] = new SKPoint(m.M21 + m.M31, m.M22 + m.M32);
            texCoords[v] = new SKPoint(source.X, source.Y);
            texCoords[v + 1] = new SKPoint(right, source.Y);
            texCoords[v + 2] = new SKPoint(right, bottom);
            texCoords[v + 3] = new SKPoint(source.X, source.Y);
            texCoords[v + 4] = new SKPoint(right, bottom);
            texCoords[v + 5] = new SKPoint(source.X, bottom);
            var color = (SKColor)instance.Tint.Argb;
            tinted |= color != SKColors.White;
            colors.AsSpan(v, 6).Fill(color);
            v += 6;
        }

        return tinted;
    }

    private void EnsureCapacity(int vertexCount)
    {
        if (_positions.Length >= vertexCount)
            return;
        var capacity = (int)Math.Min(Array.MaxLength, Math.Max(vertexCount, _positions.Length * 2L));
        _positions = GC.AllocateUninitializedArray<SKPoint>(capacity, pinned: true);
        _texCoords = GC.AllocateUninitializedArray<SKPoint>(capacity, pinned: true);
        _colors = GC.AllocateUninitializedArray<SKColor>(capacity, pinned: true);
    }

    private static ushort[] CreateQuadIndices()
    {
        var indices = GC.AllocateUninitializedArray<ushort>(MaxIndexedQuads * 6, pinned: true);
        for (var quad = 0; quad < MaxIndexedQuads; quad++)
        {
            var i = quad * 6;
            var v = (ushort)(quad * 4);
            indices[i] = v;
            indices[i + 1] = (ushort)(v + 1);
            indices[i + 2] = (ushort)(v + 2);
            indices[i + 3] = v;
            indices[i + 4] = (ushort)(v + 2);
            indices[i + 5] = (ushort)(v + 3);
        }

        return indices;
    }
}
