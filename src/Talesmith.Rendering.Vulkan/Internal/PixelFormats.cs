using System.Runtime.InteropServices;

namespace Talesmith.Rendering.Vulkan.Internal;

internal static class PixelFormats
{
    /// <summary>Swaps the red and blue channels of 8-bit four-channel pixels.</summary>
    public static void BgraToRgba(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var from = MemoryMarshal.Cast<byte, uint>(source);
        var to = MemoryMarshal.Cast<byte, uint>(destination);
        for (var i = 0; i < from.Length; i++)
        {
            var pixel = from[i];
            to[i] = (pixel & 0xFF00FF00) | ((pixel >> 16) & 0xFF) | ((pixel & 0xFF) << 16);
        }
    }
}
