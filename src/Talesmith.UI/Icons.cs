using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace Talesmith.UI;

/// <summary>Line icons drawn on a 24×24 grid, intended for stroke rendering with round caps and joins.</summary>
/// <remarks>
/// Each geometry is created on first use and belongs to the thread that created it, so ask for icons on the UI thread. Code on other threads, such
/// as the game thread, passes icon names and checks them with <see cref="Exists"/>.
/// </remarks>
public static partial class Icons
{
    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    public static Geometry Hexagon => Get(static () => ["M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"]);

    public static Geometry Brush => Get(static () => [
        "m9.06 11.9 8.07-8.06a2.85 2.85 0 1 1 4.03 4.03l-8.06 8.08",
        "M7.07 14.94c-1.66 0-3 1.35-3 3.02 0 1.33-2.5 1.52-2 2.02 1.08 1.1 2.49 2.02 4 2.02 2.2 0 4-1.8 4-4.04a3.01 3.01 0 0 0-3-3.02z"]);

    public static Geometry Eraser => Get(static () => [
        "m7 21-4.3-4.3c-1-1-1-2.5 0-3.4l9.6-9.6c1-1 2.5-1 3.4 0l5.6 5.6c1 1 1 2.5 0 3.4L13 21",
        "M22 21H7",
        "m5 11 9 9"]);

    public static Geometry PenLine => Get(static () => ["M12 20h9", "M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4Z"]);

    public static Geometry Square => Get(static () => [Rect(3, 3, 18, 18, 2)]);

    public static Geometry SquareDashed => Get(static () => [
        "M5 3a2 2 0 0 0-2 2", "M19 3a2 2 0 0 1 2 2", "M21 19a2 2 0 0 1-2 2", "M5 21a2 2 0 0 1-2-2",
        "M9 3h1", "M9 21h1", "M14 3h1", "M14 21h1", "M3 9v1", "M21 9v1", "M3 14v1", "M21 14v1"]);

    public static Geometry Circle => Get(static () => [Circ(12, 12, 10)]);

    public static Geometry PaintBucket => Get(static () => [
        "m19 11-8-8-8.6 8.6a2 2 0 0 0 0 2.8l5.2 5.2c.8.8 2 .8 2.8 0L19 11Z",
        "m5 2 5 5",
        "M2 13h15",
        "M22 20a2 2 0 1 1-4 0c0-1.6 1.7-2.4 2-4 .3 1.6 2 2.4 2 4Z"]);

    public static Geometry Pipette => Get(static () => [
        "m2 22 1-1h3l9-9",
        "M3 21v-3l9-9",
        "m15 6 3.4-3.4a2.1 2.1 0 1 1 3 3L18 9l.4.4a2.1 2.1 0 1 1-3 3l-3.8-3.8a2.1 2.1 0 1 1 3-3l.4.4Z"]);

    public static Geometry Wand => Get(static () => [
        "M15 4V2", "M15 16v-2", "M8 9h2", "M20 9h2",
        "M17.8 11.8 19 13", "M17.8 6.2 19 5", "M12.2 6.2 11 5",
        "m3 21 9-9"]);

    public static Geometry MousePointer => Get(static () => ["M4.04 4.69a.5.5 0 0 1 .65-.65l16 6.5a.5.5 0 0 1-.06.95l-6.12 1.58a2 2 0 0 0-1.44 1.44l-1.58 6.12a.5.5 0 0 1-.95.06z"]);

    public static Geometry Move => Get(static () => [
        "M5 9l-3 3 3 3", "M9 5l3-3 3 3", "M15 19l-3 3-3-3", "M19 9l3 3-3 3",
        "M2 12h20", "M12 2v20"]);

    public static Geometry Hand => Get(static () => [
        "M18 11V6a2 2 0 0 0-4 0",
        "M14 10V4a2 2 0 0 0-4 0v2",
        "M10 10.5V6a2 2 0 0 0-4 0v8",
        "M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15"]);

    public static Geometry MapPin => Get(static () => [
        "M20 10c0 5-5.54 10.19-7.4 11.8a1 1 0 0 1-1.2 0C9.54 20.19 4 15 4 10a8 8 0 0 1 16 0",
        Circ(12, 10, 3)]);

    public static Geometry Pentagon => Get(static () => ["M3.5 8.7c-.7.5-1 1.4-.7 2.2l2.8 8.7c.3.8 1 1.4 1.9 1.4h9.1c.9 0 1.6-.6 1.9-1.4l2.8-8.7c.3-.8 0-1.7-.7-2.2l-7.4-5.3a2.1 2.1 0 0 0-2.4 0Z"]);

    public static Geometry Undo => Get(static () => ["M9 14 4 9l5-5", "M4 9h10.5a5.5 5.5 0 0 1 0 11H11"]);

    public static Geometry Redo => Get(static () => ["m15 14 5-5-5-5", "M20 9H9.5a5.5 5.5 0 0 0 0 11H13"]);

    public static Geometry Eye => Get(static () => [
        "M2.06 12.35a1 1 0 0 1 0-.7 10.75 10.75 0 0 1 19.88 0 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-19.88 0",
        Circ(12, 12, 3)]);

    public static Geometry EyeOff => Get(static () => [
        "M10.73 5.08a10.74 10.74 0 0 1 11.21 6.57 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-1.45 2.49",
        "M14.08 14.16a3 3 0 0 1-4.24-4.24",
        "M17.48 17.5a10.75 10.75 0 0 1-15.42-5.15 1 1 0 0 1 0-.7 10.75 10.75 0 0 1 4.45-5.14",
        "m2 2 20 20"]);

    public static Geometry Lock => Get(static () => [Rect(3, 11, 18, 11, 2), "M7 11V7a5 5 0 0 1 10 0v4"]);

    public static Geometry Unlock => Get(static () => [Rect(3, 11, 18, 11, 2), "M7 11V7a5 5 0 0 1 9.9-1"]);

    public static Geometry Layers => Get(static () => [
        "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z",
        "m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65",
        "m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"]);

    public static Geometry Plus => Get(static () => ["M5 12h14", "M12 5v14"]);

    public static Geometry Minus => Get(static () => ["M5 12h14"]);

    public static Geometry Trash => Get(static () => [
        "M3 6h18",
        "M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6",
        "M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2",
        "M10 11v6",
        "M14 11v6"]);

    public static Geometry Copy => Get(static () => [Rect(8, 8, 14, 14, 2), "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"]);

    public static Geometry Cut => Get(static () => [
        Circ(6, 6, 3), Circ(6, 18, 3),
        "M20 4 8.12 15.88", "M14.47 14.48 20 20", "M8.12 8.12 12 12"]);

    public static Geometry Paste => Get(static () => [
        Rect(8, 2, 8, 4, 1),
        "M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"]);

    public static Geometry ChevronDown => Get(static () => ["m6 9 6 6 6-6"]);

    public static Geometry ChevronUp => Get(static () => ["m18 15-6-6-6 6"]);

    public static Geometry ChevronRight => Get(static () => ["m9 18 6-6-6-6"]);

    public static Geometry ChevronLeft => Get(static () => ["m15 18-6-6 6-6"]);

    public static Geometry ArrowUp => Get(static () => ["m5 12 7-7 7 7", "M12 19V5"]);

    public static Geometry ArrowDown => Get(static () => ["M12 5v14", "m19 12-7 7-7-7"]);

    public static Geometry Settings => Get(static () => [
        "M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z",
        Circ(12, 12, 3)]);

    public static Geometry Sun => Get(static () => [
        Circ(12, 12, 4),
        "M12 2v2", "M12 20v2", "m4.93 4.93 1.41 1.41", "m17.66 17.66 1.41 1.41",
        "M2 12h2", "M20 12h2", "m6.34 17.66-1.41 1.41", "m19.07 4.93-1.41 1.41"]);

    public static Geometry Moon => Get(static () => ["M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"]);

    public static Geometry Monitor => Get(static () => [Rect(2, 3, 20, 14, 2), "M8 21h8", "M12 17v4"]);

    public static Geometry MonitorSmartphone => Get(static () =>
        ["M18 8V6a2 2 0 0 0-2-2H4a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h8", "M10 19v-3.96 3.15", "M7 19h5", Rect(16, 12, 6, 10, 2)]);

    public static Geometry FolderOpen => Get(static () => ["m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"]);

    public static Geometry Save => Get(static () => [
        "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z",
        "M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7",
        "M7 3v4a1 1 0 0 0 1 1h7"]);

    public static Geometry FilePlus => Get(static () => [
        "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z",
        "M14 2v4a2 2 0 0 0 2 2h4",
        "M9 15h6",
        "M12 18v-6"]);

    public static Geometry Import => Get(static () => [
        "M12 3v12", "m8 11 4 4 4-4",
        "M8 5H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-4"]);

    public static Geometry Export => Get(static () => [
        "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", "m17 8-5-5-5 5", "M12 3v12"]);

    public static Geometry Grid => Get(static () => [Rect(3, 3, 18, 18, 2), "M3 9h18", "M3 15h18", "M9 3v18", "M15 3v18"]);

    public static Geometry ZoomIn => Get(static () => [Circ(11, 11, 8), "m21 21-4.3-4.3", "M11 8v6", "M8 11h6"]);

    public static Geometry ZoomOut => Get(static () => [Circ(11, 11, 8), "m21 21-4.3-4.3", "M8 11h6"]);

    public static Geometry Maximize => Get(static () => [
        "M8 3H5a2 2 0 0 0-2 2v3", "M21 8V5a2 2 0 0 0-2-2h-3",
        "M3 16v3a2 2 0 0 0 2 2h3", "M16 21h3a2 2 0 0 0 2-2v-3"]);

    public static Geometry Activity => Get(static () => ["M22 12h-4l-3 9L9 3l-3 9H2"]);

    public static Geometry Gauge => Get(static () => ["m12 14 4-4", "M3.34 19a10 10 0 1 1 17.32 0"]);

    public static Geometry X => Get(static () => ["M18 6 6 18", "m6 6 12 12"]);

    public static Geometry Check => Get(static () => ["M20 6 9 17l-5-5"]);

    public static Geometry Search => Get(static () => [Circ(11, 11, 8), "m21 21-4.3-4.3"]);

    public static Geometry Menu => Get(static () => ["M4 12h16", "M4 6h16", "M4 18h16"]);

    public static Geometry MoreHorizontal => Get(static () => [Circ(12, 12, 1), Circ(19, 12, 1), Circ(5, 12, 1)]);

    public static Geometry Info => Get(static () => [Circ(12, 12, 10), "M12 16v-4", "M12 8h.01"]);

    public static Geometry AlertTriangle => Get(static () => [
        "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3",
        "M12 9v4",
        "M12 17h.01"]);

    public static Geometry AlertCircle => Get(static () => [Circ(12, 12, 10), "M12 8v4", "M12 16h.01"]);

    public static Geometry CheckCircle => Get(static () => [Circ(12, 12, 10), "m9 12 2 2 4-4"]);

    public static Geometry Keyboard => Get(static () => [
        Rect(2, 4, 20, 16, 2),
        "M6 8h.01", "M10 8h.01", "M14 8h.01", "M18 8h.01",
        "M8 12h.01", "M12 12h.01", "M16 12h.01",
        "M7 16h10"]);

    public static Geometry Image => Get(static () => [Rect(3, 3, 18, 18, 2), Circ(9, 9, 2), "m21 15-3.09-3.09a2 2 0 0 0-2.82 0L6 21"]);

    public static Geometry Palette => Get(static () => [
        "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.93 0 1.65-.75 1.65-1.69 0-.44-.18-.84-.44-1.13-.29-.29-.44-.65-.44-1.13a1.64 1.64 0 0 1 1.67-1.67h2c3.05 0 5.55-2.5 5.55-5.55C21.97 6.01 17.46 2 12 2z",
        Circ(13.5, 6.5, 0.5), Circ(17.5, 10.5, 0.5), Circ(8.5, 7.5, 0.5), Circ(6.5, 12.5, 0.5)]);

    public static Geometry RotateCw => Get(static () => ["M21 12a9 9 0 1 1-9-9c2.52 0 4.93 1 6.74 2.74L21 8", "M21 3v5h-5"]);

    public static Geometry RotateCcw => Get(static () => ["M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", "M3 3v5h5"]);

    public static Geometry FlipHorizontal => Get(static () => [
        "m3 7 5 5-5 5V7", "m21 7-5 5 5 5V7",
        "M12 20v2", "M12 14v2", "M12 8v2", "M12 2v2"]);

    public static Geometry FlipVertical => Get(static () => [
        "m17 3-5 5-5-5h10", "m17 21-5-5-5 5h10",
        "M4 12H2", "M10 12H8", "M16 12h-2", "M22 12h-2"]);

    public static Geometry Play => Get(static () => ["M6 3 20 12 6 21 6 3z"]);

    public static Geometry Pause => Get(static () => [Rect(14, 4, 4, 16, 1), Rect(6, 4, 4, 16, 1)]);

    public static Geometry Cpu => Get(static () => [
        Rect(4, 4, 16, 16, 2), Rect(9, 9, 6, 6, 1),
        "M15 2v2", "M15 20v2", "M2 15h2", "M2 9h2",
        "M20 15h2", "M20 9h2", "M9 2v2", "M9 20v2"]);

    public static Geometry Box => Get(static () => [
        "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z",
        "m3.3 7 8.7 5 8.7-5",
        "M12 22V12"]);

    public static Geometry Tag => Get(static () => [
        "M12.59 2.59A2 2 0 0 0 11.17 2H4a2 2 0 0 0-2 2v7.17a2 2 0 0 0 .59 1.42l8.7 8.7a2.43 2.43 0 0 0 3.42 0l6.58-6.58a2.43 2.43 0 0 0 0-3.42z",
        Circ(7.5, 7.5, 0.5)]);

    public static Geometry Sliders => Get(static () => [
        "M21 4h-7", "M10 4H3", "M21 12h-9", "M8 12H3", "M21 20h-5", "M12 20H3",
        "M14 2v4", "M8 10v4", "M16 18v4"]);

    public static Geometry PanelLeft => Get(static () => [Rect(3, 3, 18, 18, 2), "M9 3v18"]);

    public static Geometry PanelRight => Get(static () => [Rect(3, 3, 18, 18, 2), "M15 3v18"]);

    public static Geometry PanelBottom => Get(static () => [Rect(3, 3, 18, 18, 2), "M3 15h18"]);

    public static Geometry Bug => Get(static () => [
        "m8 2 1.88 1.88", "M14.12 3.88 16 2",
        "M9 7.13v-1a3 3 0 1 1 6 0v1",
        "M12 20c-3.3 0-6-2.7-6-6v-3a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v3c0 3.3-2.7 6-6 6",
        "M12 20v-9",
        "M6.53 9C4.6 8.8 3 7.1 3 5", "M6 13H2", "M3 21c0-2.1 1.7-3.9 3.8-4",
        "M20.97 5c0 2.1-1.6 3.8-3.5 4", "M22 13h-4", "M17.2 17c2.1.1 3.8 1.9 3.8 4"]);

    public static Geometry Sparkles => Get(static () => [
        "M9.94 15.5A2 2 0 0 0 8.5 14.06l-6.13-1.58a.5.5 0 0 1 0-.96L8.5 9.94A2 2 0 0 0 9.94 8.5l1.58-6.13a.5.5 0 0 1 .96 0l1.58 6.13a2 2 0 0 0 1.44 1.44l6.13 1.58a.5.5 0 0 1 0 .96l-6.13 1.58a2 2 0 0 0-1.44 1.44l-1.58 6.13a.5.5 0 0 1-.96 0z",
        "M20 3v4", "M22 5h-4", "M4 17v2", "M5 18H3"]);

    public static Geometry Map => Get(static () => [
        "M14.11 5.55a2 2 0 0 0 1.78 0l3.66-1.83A1 1 0 0 1 21 4.62v12.76a1 1 0 0 1-.55.9l-4.56 2.27a2 2 0 0 1-1.78 0l-4.22-2.1a2 2 0 0 0-1.78 0l-3.66 1.83A1 1 0 0 1 3 19.38V6.62a1 1 0 0 1 .55-.9l4.56-2.27a2 2 0 0 1 1.78 0z",
        "M15 5.76v15",
        "M9 3.24v15"]);

    private static string Circ(double cx, double cy, double r) =>
        string.Create(CultureInfo.InvariantCulture, $"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0Z");

    private static string Rect(double x, double y, double w, double h, double rx) =>
        string.Create(CultureInfo.InvariantCulture,
            $"M{x + rx} {y}h{w - (2 * rx)}a{rx} {rx} 0 0 1 {rx} {rx}v{h - (2 * rx)}a{rx} {rx} 0 0 1 {-rx} {rx}h{-(w - (2 * rx))}a{rx} {rx} 0 0 1 {-rx} {-rx}v{-(h - (2 * rx))}a{rx} {rx} 0 0 1 {rx} {-rx}Z");

    private static Geometry Get(Func<string[]> segments, [CallerMemberName] string name = "")
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out var geometry))
            {
                geometry = Geometry.Parse(string.Join(' ', segments().Select(ToAbsoluteStart)));
                Cache[name] = geometry;
            }

            return geometry;
        }
    }

    // Each segment is an independent path, so a leading relative move must not chain from the previous segment.
    private static string ToAbsoluteStart(string segment)
    {
        if (!segment.StartsWith('m'))
        {
            return segment;
        }

        var index = 1;
        var x = ReadNumber(segment, ref index);
        var y = ReadNumber(segment, ref index);
        var rest = segment[index..].TrimStart(' ', ',');
        var continuesWithPoints = rest.Length > 0 && !char.IsLetter(rest[0]);

        return $"M{x} {y}{(continuesWithPoints ? " l" : " ")}{rest}";
    }

    private static string ReadNumber(string text, ref int index)
    {
        while (index < text.Length && text[index] is ' ' or ',')
        {
            index++;
        }

        var start = index;
        if (index < text.Length && text[index] is '-' or '+')
        {
            index++;
        }

        var seenDot = false;
        while (index < text.Length && (char.IsDigit(text[index]) || (text[index] == '.' && !seenDot)))
        {
            seenDot |= text[index] == '.';
            index++;
        }

        return text[start..index];
    }
}
