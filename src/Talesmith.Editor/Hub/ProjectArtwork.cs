using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Talesmith.Assets.Database;
using Talesmith.Editor.Hosting;

namespace Talesmith.Editor.Hub;

/// <summary>How a project is pictured in the hub and while it opens: the scene view it showed when it last closed, or a gradient picked by
/// its name behind its initials.</summary>
public static class ProjectArtwork
{
    private static readonly (Color From, Color To)[] Gradients =
    [
        (Color.Parse("#8B8EFA"), Color.Parse("#5B4FE0")),
        (Color.Parse("#34D399"), Color.Parse("#0E7490")),
        (Color.Parse("#FBBF24"), Color.Parse("#F97316")),
        (Color.Parse("#F472B6"), Color.Parse("#BE185D")),
        (Color.Parse("#38BDF8"), Color.Parse("#1D4ED8")),
        (Color.Parse("#A3E635"), Color.Parse("#15803D"))
    ];

    /// <summary>A diagonal gradient that is the same for the same name in every session.</summary>
    public static IBrush Gradient(string name)
    {
        var (from, to) = Gradients[StableHash(name) % (uint)Gradients.Length];
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
        };
    }

    /// <summary>The first letters of the name's first two words, such as "HQ" for "Hex Quest".</summary>
    public static string Initials(string name) =>
        string.Concat(name.Split(' ', '-', '_').Where(w => w.Length > 0).Take(2).Select(w => char.ToUpperInvariant(w[0])));

    /// <summary>The scene view the editor saved when the project in <paramref name="projectFolder"/> last closed, or null.</summary>
    public static Bitmap? LoadThumbnail(string projectFolder)
    {
        var file = Path.Combine(projectFolder, AssetDatabaseOptions.StateFolderName, EditorHost.ThumbnailFile);
        try
        {
            return File.Exists(file) ? new Bitmap(file) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The path with the home folder shortened to ~.</summary>
    public static string DisplayPath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal) ? "~" + path[home.Length..] : path;
    }

    /// <summary>FNV-1a, ignoring case; unlike <see cref="string.GetHashCode()"/>, it gives the same value in every process.</summary>
    private static uint StableHash(string text)
    {
        var hash = 2166136261;
        foreach (var c in text)
            hash = unchecked((hash ^ char.ToUpperInvariant(c)) * 16777619);
        return hash;
    }
}
