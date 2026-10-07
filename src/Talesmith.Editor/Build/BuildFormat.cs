using System.Globalization;
using Avalonia.Media;
using Talesmith.Build;

namespace Talesmith.Editor.Build;

/// <summary>Formats sizes, durations and targets for the build UI.</summary>
public static class BuildFormat
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 30):N2} GB"),
        >= 1L << 20 => string.Create(CultureInfo.CurrentCulture, $"{bytes / (double)(1L << 20):N1} MB"),
        >= 1L << 10 => string.Create(CultureInfo.CurrentCulture, $"{bytes / 1024.0:N0} KB"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{bytes:N0} B")
    };

    public static string Duration(TimeSpan duration) => duration.TotalSeconds < 1
        ? string.Create(CultureInfo.CurrentCulture, $"{duration.TotalMilliseconds:N0} ms")
        : duration.TotalSeconds < 60
        ? string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:N1} s")
        : string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds} s");

    /// <summary>The icon of a target's operating system.</summary>
    public static Geometry PlatformIcon(BuildPlatform platform) => platform switch
    {
        BuildPlatform.Windows => Windows,
        _ => Linux
    };

    private static readonly Geometry Windows = Geometry.Parse("M3 5.6 10.5 4.5v7H3z M13 4.1 21 3v8.5h-8z M3 13h7.5v6.5L3 18.4z M13 13h8v8l-8-1.1z");

    private static readonly Geometry Linux = Geometry.Parse("M4 17 10 11 4 5 M12 19h8");
}
