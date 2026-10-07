using Talesmith.Mathematics;

namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Where a frame's view lands in the output image, in whole pixels; the scene targets are the size of the view.</summary>
internal readonly record struct ViewPlacement(int X, int Y, int Width, int Height, int OutputWidth, int OutputHeight, Color Border)
{
    /// <summary>The frame's view rectangle within an output, or the whole output when the frame was laid out for another size.</summary>
    public static ViewPlacement Of(RenderFrame frame, int outputWidth, int outputHeight)
    {
        var rect = frame.View.ViewRect;
        var left = Math.Clamp((int)rect.X, 0, outputWidth);
        var top = Math.Clamp((int)rect.Y, 0, outputHeight);
        var right = Math.Clamp((int)(rect.X + rect.Width), 0, outputWidth);
        var bottom = Math.Clamp((int)(rect.Y + rect.Height), 0, outputHeight);
        return right > left && bottom > top
            ? new ViewPlacement(left, top, right - left, bottom - top, outputWidth, outputHeight, frame.BorderColor)
            : new ViewPlacement(0, 0, outputWidth, outputHeight, outputWidth, outputHeight, frame.BorderColor);
    }

    /// <summary>Whether part of the output lies outside the view and shows the border color.</summary>
    public bool HasBorders => Width < OutputWidth || Height < OutputHeight;

    /// <summary>The border color as one premultiplied BGRA pixel, the layout of readback buffers.</summary>
    public uint BorderBgra
    {
        get
        {
            var alpha = Border.A;
            return Premultiply(Border.B, alpha) | Premultiply(Border.G, alpha) << 8 | Premultiply(Border.R, alpha) << 16 | (uint)alpha << 24;
        }
    }

    private static uint Premultiply(byte channel, byte alpha) => (uint)((channel * alpha + 127) / 255);
}
