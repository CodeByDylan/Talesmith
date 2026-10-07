using System.Runtime.InteropServices;
using SkiaSharp;

namespace Talesmith.Rendering.Skia;

/// <summary>Direct calls into SkiaSharp's native library for per-frame work, where SkiaSharp's wrappers allocate a managed object per call.</summary>
internal static class SkiaNative
{
    private const string Library = "libSkiaSharp";

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_vertices_make_copy(SKVertexMode mode, int vertexCount, nint positions, nint texCoords, nint colors,
        int indexCount, nint indices);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_vertices_unref(nint vertices);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_canvas_draw_vertices(nint canvas, nint vertices, SKBlendMode mode, nint paint);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_surface_new_image_snapshot(nint surface);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_image_unref(nint image);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_image_make_shader(nint image, SKShaderTileMode tileX, SKShaderTileMode tileY, in SKSamplingOptions sampling, nint localMatrix);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_image_make_shader(nint image, SKShaderTileMode tileX, SKShaderTileMode tileY, in SKSamplingOptions sampling, in SKMatrix localMatrix);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_image_new_raster_copy(in ImageInfo info, nint pixels, nint rowBytes);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_image_new_raster_copy(in ImageInfo info, in byte pixels, nint rowBytes);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_shader_unref(nint shader);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_shader_new_radial_gradient_color4f(in SKPoint center, float radius, SKColorF[] colors, nint colorSpace, float[] positions,
        int count, SKShaderTileMode mode, nint localMatrix);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_shader_with_local_matrix(nint shader, in SKMatrix localMatrix);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_shader_new_color4f(in SKColorF color, nint colorSpace);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_shader_new_blend(SKBlendMode mode, nint destination, nint source);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_maskfilter_new_blur_with_flags(SKBlurStyle style, float sigma, [MarshalAs(UnmanagedType.I1)] bool respectTransform);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_maskfilter_unref(nint maskFilter);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_paint_set_maskfilter(nint paint, nint maskFilter);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_data_new_with_copy(in byte source, nint length);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_data_unref(nint data);

    [DllImport(Library, ExactSpelling = true)]
    public static extern nint sk_runtimeeffect_make_shader(nint effect, nint uniforms, in nint children, nint childCount, nint localMatrix);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_runtimeeffect_get_uniform_from_index(nint effect, int index, out RuntimeEffectUniform uniform);

    [DllImport(Library, ExactSpelling = true)]
    public static extern void sk_paint_set_shader(nint paint, nint shader);

    /// <summary>Mirrors <c>sk_imageinfo_t</c>; <see cref="ColorType"/> uses the native numbering, not <see cref="SKColorType"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ImageInfo(int width, int height, int colorType, SKAlphaType alphaType)
    {
        public const int Alpha8 = 1;
        public const int RgbaF16 = 15;

        public nint ColorSpace;
        public int Width = width;
        public int Height = height;
        public int ColorType = colorType;
        public SKAlphaType AlphaType = alphaType;
    }

    /// <summary>Mirrors <c>sk_runtimeeffect_uniform_t</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RuntimeEffectUniform
    {
        public nint Name;
        public nint NameLength;
        public nint Offset;
        public int Type;
        public int Count;
        public uint Flags;
    }
}
