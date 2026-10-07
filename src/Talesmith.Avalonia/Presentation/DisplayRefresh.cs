using System.Runtime.InteropServices;
using Avalonia;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Reads a monitor's refresh rate from XRandR, which Avalonia does not expose.</summary>
/// <remarks>Works under X11 and XWayland. Returns null elsewhere, or when the X server cannot be reached.</remarks>
internal static class DisplayRefresh
{
    private const string X11 = "libX11.so.6";
    private const string XRandR = "libXrandr.so.2";
    private const ulong Interlace = 0x10;
    private const ulong DoubleScan = 0x20;

    /// <summary>Gets the refresh rate, in hertz, of the monitor that shows the center of <paramref name="screenBounds"/>.</summary>
    /// <param name="screenBounds">A screen's bounds in physical pixels, as Avalonia reports them.</param>
    public static double? RateOf(PixelRect screenBounds)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            return null;

        try
        {
            return Query(screenBounds.Center);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static double? Query(PixelPoint point)
    {
        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return null;

        try
        {
            var resourcesPointer = XRRGetScreenResourcesCurrent(display, XDefaultRootWindow(display));
            if (resourcesPointer == IntPtr.Zero)
                return null;

            try
            {
                var resources = Marshal.PtrToStructure<ScreenResources>(resourcesPointer);
                for (var i = 0; i < resources.CrtcCount; i++)
                {
                    var crtc = (ulong)Marshal.ReadInt64(resources.Crtcs, i * sizeof(ulong));
                    if (CrtcRate(display, resourcesPointer, resources, crtc, point) is { } rate)
                        return rate;
                }

                return null;
            }
            finally
            {
                XRRFreeScreenResources(resourcesPointer);
            }
        }
        finally
        {
            _ = XCloseDisplay(display);
        }
    }

    private static double? CrtcRate(IntPtr display, IntPtr resourcesPointer, in ScreenResources resources, ulong crtc, PixelPoint point)
    {
        var infoPointer = XRRGetCrtcInfo(display, resourcesPointer, crtc);
        if (infoPointer == IntPtr.Zero)
            return null;

        try
        {
            var info = Marshal.PtrToStructure<CrtcInfo>(infoPointer);
            if (info.Mode == 0 || !new PixelRect(info.X, info.Y, (int)info.Width, (int)info.Height).Contains(point))
                return null;

            var modeSize = Marshal.SizeOf<ModeInfo>();
            for (var i = 0; i < resources.ModeCount; i++)
            {
                var mode = Marshal.PtrToStructure<ModeInfo>(resources.Modes + i * modeSize);
                if (mode.Id != info.Mode || mode.HTotal == 0 || mode.VTotal == 0 || mode.DotClock == 0)
                    continue;
                var lines = (double)mode.VTotal;
                if ((mode.ModeFlags & DoubleScan) != 0)
                    lines *= 2;
                if ((mode.ModeFlags & Interlace) != 0)
                    lines /= 2;
                return mode.DotClock / (mode.HTotal * lines);
            }

            return null;
        }
        finally
        {
            XRRFreeCrtcInfo(infoPointer);
        }
    }

    [DllImport(X11, ExactSpelling = true)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(X11, ExactSpelling = true)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(X11, ExactSpelling = true)]
    private static extern ulong XDefaultRootWindow(IntPtr display);

    [DllImport(XRandR, ExactSpelling = true)]
    private static extern IntPtr XRRGetScreenResourcesCurrent(IntPtr display, ulong window);

    [DllImport(XRandR, ExactSpelling = true)]
    private static extern void XRRFreeScreenResources(IntPtr resources);

    [DllImport(XRandR, ExactSpelling = true)]
    private static extern IntPtr XRRGetCrtcInfo(IntPtr display, IntPtr resources, ulong crtc);

    [DllImport(XRandR, ExactSpelling = true)]
    private static extern void XRRFreeCrtcInfo(IntPtr info);

    /// <summary>XRRScreenResources.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ScreenResources
    {
        public readonly ulong Timestamp;
        public readonly ulong ConfigTimestamp;
        public readonly int CrtcCount;
        public readonly IntPtr Crtcs;
        public readonly int OutputCount;
        public readonly IntPtr Outputs;
        public readonly int ModeCount;
        public readonly IntPtr Modes;
    }

    /// <summary>XRRCrtcInfo.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CrtcInfo
    {
        public readonly ulong Timestamp;
        public readonly int X;
        public readonly int Y;
        public readonly uint Width;
        public readonly uint Height;
        public readonly ulong Mode;
        public readonly ushort Rotation;
        public readonly int OutputCount;
        public readonly IntPtr Outputs;
        public readonly ushort Rotations;
        public readonly int PossibleCount;
        public readonly IntPtr Possible;
    }

    /// <summary>XRRModeInfo.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ModeInfo
    {
        public readonly ulong Id;
        public readonly uint Width;
        public readonly uint Height;
        public readonly ulong DotClock;
        public readonly uint HSyncStart;
        public readonly uint HSyncEnd;
        public readonly uint HTotal;
        public readonly uint HSkew;
        public readonly uint VSyncStart;
        public readonly uint VSyncEnd;
        public readonly uint VTotal;
        public readonly IntPtr Name;
        public readonly uint NameLength;
        public readonly ulong ModeFlags;
    }
}
