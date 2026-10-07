using Avalonia.Media;

namespace Talesmith.UI;

public static partial class Icons
{
    public static Geometry Stop => Get(static () => [Rect(5, 5, 14, 14, 2)]);

    public static Geometry StepForward => Get(static () => ["M6 4v16", "M10 4 20 12 10 20Z"]);

    public static Geometry SkipBack => Get(static () => ["M19 20 9 12 19 4Z", "M5 19V5"]);

    public static Geometry SkipForward => Get(static () => ["M5 4 15 12 5 20Z", "M19 5v14"]);

    public static Geometry ListTree => Get(static () => [
        "M21 12h-8", "M21 6H8", "M21 18h-8",
        "M3 6v4c0 1.1.9 2 2 2h3", "M3 10v6c0 1.1.9 2 2 2h3"]);

    public static Geometry Puzzle => Get(static () => [
        "M19.44 7.85c-.05.32.06.65.29.88l1.57 1.57c.47.47.7 1.09.7 1.7s-.23 1.24-.7 1.7l-1.61 1.62a.98.98 0 0 1-.84.27c-.47-.07-.8-.48-.97-.92a2.5 2.5 0 1 0-3.21 3.21c.44.17.85.5.92.97a.98.98 0 0 1-.27.84l-1.61 1.61a2.4 2.4 0 0 1-1.71.7 2.4 2.4 0 0 1-1.7-.7l-1.57-1.57a1.03 1.03 0 0 0-.88-.29c-.49.07-.84.5-1.02.97a2.5 2.5 0 1 1-3.24-3.24c.46-.18.9-.53.97-1.02a1.03 1.03 0 0 0-.29-.88l-1.57-1.57A2.4 2.4 0 0 1 2 12c0-.62.24-1.23.7-1.7l1.53-1.53c.24-.24.58-.35.92-.3.51.08.88.53 1.07 1.01a2.5 2.5 0 1 0 3.26-3.26c-.48-.19-.93-.56-1.01-1.07-.05-.34.06-.68.3-.92l1.53-1.53A2.4 2.4 0 0 1 12 2c.62 0 1.23.24 1.7.7l1.57 1.57c.23.23.56.34.88.29.49-.07.84-.5 1.02-.97a2.5 2.5 0 1 1 3.24 3.24c-.46.18-.9.53-.97 1.02Z"]);

    public static Geometry Camera => Get(static () => [
        "M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3z",
        Circ(12, 13, 3)]);

    public static Geometry Lightbulb => Get(static () => [
        "M15 14c.2-1 .7-1.7 1.5-2.5 1-.9 1.5-2.2 1.5-3.5A6 6 0 0 0 6 8c0 1 .2 2.2 1.5 3.5.7.7 1.3 1.5 1.5 2.5",
        "M9 18h6", "M10 22h4"]);

    public static Geometry Flashlight => Get(static () => [
        "M18 6c0 2-2 2-2 4v10a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2V10c0-2-2-2-2-4V2h12z",
        "M6 6h12", "M12 12v.01"]);

    public static Geometry Code => Get(static () => ["m16 18 6-6-6-6", "m8 6-6 6 6 6"]);

    public static Geometry FileCode => Get(static () => [
        "M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z",
        "M14 2v4a2 2 0 0 0 2 2h4",
        "M10 12.5 8 15l2 2.5", "m14 12.5 2 2.5-2 2.5"]);

    public static Geometry Music => Get(static () => [
        "M9 18V5l12-2v13", "M9 18a3 3 0 1 1-6 0a3 3 0 1 1 6 0", "M21 16a3 3 0 1 1-6 0a3 3 0 1 1 6 0"]);

    public static Geometry Type => Get(static () => ["M4 7V4h16v3", "M9 20h6", "M12 4v16"]);

    public static Geometry Languages => Get(static () => [
        "m5 8 6 6", "m4 14 6-6 2-3", "M2 5h12", "M7 2h1", "m22 22-5-10-5 10", "M14 18h6"]);

    public static Geometry Volume => Get(static () => [
        "M11 4.7a.7.7 0 0 0-1.2-.5L6.41 7.59A1.4 1.4 0 0 1 5.42 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.42a1.4 1.4 0 0 1 .99.41l3.39 3.39a.7.7 0 0 0 1.2-.5z",
        "M16 9a5 5 0 0 1 0 6", "M19.36 18.36a9 9 0 0 0 0-12.72"]);

    public static Geometry VolumeOff => Get(static () => [
        "M11 4.7a.7.7 0 0 0-1.2-.5L6.41 7.59A1.4 1.4 0 0 1 5.42 8H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h2.42a1.4 1.4 0 0 1 .99.41l3.39 3.39a.7.7 0 0 0 1.2-.5z",
        "m22 9-6 6", "m16 9 6 6"]);

    public static Geometry Folder => Get(static () => [
        "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"]);

    public static Geometry FolderPlus => Get(static () => [
        "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        "M12 10v6", "M9 13h6"]);

    public static Geometry File => Get(static () => ["M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z", "M14 2v4a2 2 0 0 0 2 2h4"]);

    public static Geometry Package => Get(static () => [
        "M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z",
        "M12 22V12", "m3.3 7 7.7 4.73a2 2 0 0 0 2 0L20.7 7", "m7.5 4.27 9 5.15"]);

    public static Geometry PackageExport => Get(static () => [
        "M21 10V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l1.5-.86",
        "M12 22V12", "m3.3 7 8.7 5 8.7-5", "M16 17h6", "m19 14 3 3-3 3"]);

    public static Geometry Clapperboard => Get(static () => [
        "M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z",
        "m6.2 5.3 3.1 3.9", "m12.4 3.4 3.1 4",
        "M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z"]);

    public static Geometry Rotate3D => Get(static () => [
        "M16.47 7.5C15.64 4.24 13.95 2 12 2 9.24 2 7 6.48 7 12s2.24 10 5 10c.34 0 .68-.07 1-.2",
        "m15.19 13.71 3.82 1.86-1.86 3.81",
        "M19 15.57c-1.8.89-4.27 1.43-7 1.43-5.52 0-10-2.24-10-5s4.48-5 10-5c4.84 0 8.87 1.72 9.8 4"]);

    public static Geometry Scale => Get(static () => ["M21 3 9 15", "M12 3H3v18h18v-9", "M16 3h5v5", "M14 15H9v-5"]);

    public static Geometry Magnet => Get(static () => [
        "m6 15-4-4 6.75-6.77a7.79 7.79 0 0 1 11 11L13 22l-4-4 6.39-6.36a2.14 2.14 0 0 0-3-3L6 15",
        "m5 8 4 4", "m12 15 4 4"]);

    public static Geometry Terminal => Get(static () => [Rect(2, 3, 20, 18, 2), "m7 10 3 3-3 3", "M13 16h4"]);

    public static Geometry Plug => Get(static () => ["M12 22v-5", "M9 8V2", "M15 8V2", "M18 8v5a4 4 0 0 1-4 4h-4a4 4 0 0 1-4-4V8Z"]);

    public static Geometry Hammer => Get(static () => [
        "m15 12-8.37 8.37a1 1 0 1 1-3-3L12 9",
        "m18 15 4-4",
        "m21.5 11.5-1.91-1.91A2 2 0 0 1 19 8.17V7l-2.26-2.26a6 6 0 0 0-4.2-1.76L9 2.96l.92.82A6.18 6.18 0 0 1 12 8.4V10l2 2h1.17a2 2 0 0 1 1.42.59L18.5 14.5"]);

    public static Geometry Filter => Get(static () => [
        "M10 20a1 1 0 0 0 .55.9l2 1A1 1 0 0 0 14 21v-7a2 2 0 0 1 .52-1.34L21.74 4.67A1 1 0 0 0 21 3H3a1 1 0 0 0-.74 1.67l7.22 7.99A2 2 0 0 1 10 14z"]);

    public static Geometry Refresh => Get(static () => [
        "M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8", "M21 3v5h-5",
        "M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16", "M8 16H3v5"]);

    public static Geometry Star => Get(static () => [
        "M11.53 2.3a.53.53 0 0 1 .95 0l2.31 4.68a2.12 2.12 0 0 0 1.6 1.16l5.16.76a.53.53 0 0 1 .3.9l-3.74 3.64a2.12 2.12 0 0 0-.61 1.88l.88 5.14a.53.53 0 0 1-.77.56l-4.62-2.43a2.12 2.12 0 0 0-1.97 0L6.4 21.01a.53.53 0 0 1-.77-.56l.88-5.14a2.12 2.12 0 0 0-.61-1.88L2.16 9.8a.53.53 0 0 1 .29-.91l5.17-.75a2.12 2.12 0 0 0 1.6-1.16z"]);

    public static Geometry Slash => Get(static () => ["M19 5 5 19"]);

    public static Geometry Dice => Get(static () => [Rect(3, 3, 18, 18, 2), "M16 8h.01", "M8 8h.01", "M8 16h.01", "M16 16h.01", "M12 12h.01"]);

    public static Geometry Shield => Get(static () => [
        "M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"]);

    public static Geometry Flag => Get(static () => ["M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z", "M4 22v-7"]);

    public static Geometry Compass => Get(static () => [Circ(12, 12, 10), "m16.24 7.76-1.8 5.41a2 2 0 0 1-1.27 1.27L7.76 16.24l1.8-5.41a2 2 0 0 1 1.27-1.27z"]);

    public static Geometry Atom => Get(static () => [
        Circ(12, 12, 1),
        "M20.2 20.2c2.04-2.03.02-7.36-4.5-11.9-4.54-4.52-9.87-6.54-11.9-4.5-2.04 2.03-.02 7.36 4.5 11.9 4.54 4.52 9.87 6.54 11.9 4.5Z",
        "M15.7 15.7c4.52-4.54 6.54-9.87 4.5-11.9-2.03-2.04-7.36-.02-11.9 4.5-4.52 4.54-6.54 9.87-4.5 11.9 2.03 2.04 7.36.02 11.9-4.5Z"]);

    public static Geometry Weight => Get(static () => [
        Circ(12, 5, 3),
        "M6.5 8a2 2 0 0 0-1.9 1.46L2.1 18.5A2 2 0 0 0 4 21h16a2 2 0 0 0 1.93-2.54L19.4 9.5A2 2 0 0 0 17.48 8Z"]);

    public static Geometry Link => Get(static () => [
        "M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71",
        "M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"]);

    public static Geometry Unlink => Get(static () => [
        "m18.84 12.25 1.72-1.71a5 5 0 0 0-.12-7.07 5 5 0 0 0-6.95 0l-1.72 1.71",
        "m5.17 11.75-1.71 1.71a5 5 0 0 0 .12 7.07 5 5 0 0 0 6.95 0l1.71-1.71",
        "M8 2v3", "M2 8h3", "M16 19v3", "M19 16h3"]);

    public static Geometry ExternalLink => Get(static () => [
        "M15 3h6v6", "M10 14 21 3", "M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"]);

    public static Geometry CopyPlus => Get(static () => [
        Rect(8, 8, 14, 14, 2), "M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        "M15 12v6", "M12 15h6"]);

    public static Geometry ChevronsLeft => Get(static () => ["m11 17-5-5 5-5", "m18 17-5-5 5-5"]);

    public static Geometry ChevronsRight => Get(static () => ["m6 17 5-5-5-5", "m13 17 5-5-5-5"]);

    public static Geometry ChevronsUp => Get(static () => ["m17 11-5-5-5 5", "m17 18-5-5-5 5"]);

    public static Geometry ChevronsDown => Get(static () => ["m7 6 5 5 5-5", "m7 13 5 5 5-5"]);

    public static Geometry ChevronsUpDown => Get(static () => ["m7 15 5 5 5-5", "m7 9 5-5 5 5"]);

    public static Geometry MoreVertical => Get(static () => [Circ(12, 12, 1), Circ(12, 5, 1), Circ(12, 19, 1)]);

    public static Geometry GripVertical => Get(static () => [
        Circ(9, 12, 1), Circ(9, 5, 1), Circ(9, 19, 1),
        Circ(15, 12, 1), Circ(15, 5, 1), Circ(15, 19, 1)]);

    public static Geometry Maximize2 => Get(static () => ["M15 3h6v6", "M9 21H3v-6", "m21 3-7 7", "m3 21 7-7"]);

    public static Geometry Minimize2 => Get(static () => ["m14 10 7-7", "M20 10h-6V4", "m3 21 7-7", "M4 14h6v6"]);

    public static Geometry Layout => Get(static () => [Rect(3, 3, 7, 9, 1), Rect(14, 3, 7, 5, 1), Rect(14, 12, 7, 9, 1), Rect(3, 16, 7, 5, 1)]);

    public static Geometry ArrowLeft => Get(static () => ["m12 19-7-7 7-7", "M19 12H5"]);

    public static Geometry ArrowRight => Get(static () => ["M5 12h14", "m12 5 7 7-7 7"]);

    public static Geometry Crosshair => Get(static () => [Circ(12, 12, 10), "M22 12h-4", "M6 12H2", "M12 6V2", "M12 22v-4"]);

    public static Geometry Spline => Get(static () => [Circ(19, 5, 2), Circ(5, 19, 2), "M5 17A12 12 0 0 1 17 5"]);

    public static Geometry Blend => Get(static () => [Circ(9, 9, 7), Circ(15, 15, 7)]);

    public static Geometry Diamond => Get(static () => [
        "M2.7 10.3a2.41 2.41 0 0 0 0 3.41l7.59 7.59a2.41 2.41 0 0 0 3.41 0l7.59-7.59a2.41 2.41 0 0 0 0-3.41l-7.59-7.59a2.41 2.41 0 0 0-3.41 0Z"]);
}
