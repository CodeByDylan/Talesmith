using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia;

namespace Talesmith.Screenshots.Docs;

/// <summary>The pixel size and default alt text of a screenshot, as the website reads them.</summary>
internal sealed record ManifestEntry(int Width, int Height, string Alt);

/// <summary>Keeps a section's <c>manifest.json</c> and its images in step with the section's screenshot list.</summary>
internal static class DocsManifest
{
    private const string FileName = "manifest.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Adds or updates a screenshot's entry as soon as its images are saved, so an interrupted run leaves no image unlisted.</summary>
    public static void Record(string folder, DocsSection section, DocsShot shot, PixelSize size)
    {
        var entries = Read(folder);
        entries[DocsCatalog.FullName(section, shot)] = new ManifestEntry(size.Width, size.Height, shot.Alt);
        Write(folder, entries);
    }

    /// <summary>Drops entries of screenshots the section no longer lists or whose images are gone, deletes images no longer listed, and refreshes alt text.</summary>
    /// <returns>The names of listed screenshots without an entry and images.</returns>
    public static IReadOnlyList<string> Prune(string folder, DocsSection section)
    {
        var previous = Read(folder);
        var entries = new SortedDictionary<string, ManifestEntry>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var shot in section.Shots)
        {
            var name = DocsCatalog.FullName(section, shot);
            if (previous.TryGetValue(name, out var entry) && HasImages(folder, shot))
                entries[name] = entry with { Alt = shot.Alt };
            else
                missing.Add(name);
        }

        Write(folder, entries);
        DeleteUnlisted(folder, section);
        return missing;
    }

    private static bool HasImages(string folder, DocsShot shot) =>
        File.Exists(Path.Combine(folder, $"{shot.Name}.dark.webp")) && File.Exists(Path.Combine(folder, $"{shot.Name}.light.webp"));

    private static SortedDictionary<string, ManifestEntry> Read(string folder)
    {
        var path = Path.Combine(folder, FileName);
        if (!File.Exists(path))
            return new SortedDictionary<string, ManifestEntry>(StringComparer.Ordinal);
        using var stream = File.OpenRead(path);
        var entries = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(stream, Options) ?? [];
        return new SortedDictionary<string, ManifestEntry>(entries, StringComparer.Ordinal);
    }

    private static void Write(string folder, SortedDictionary<string, ManifestEntry> entries)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);
        var temporary = path + ".tmp";
        using (var stream = File.Create(temporary))
            JsonSerializer.Serialize(stream, entries, Options);
        File.Move(temporary, path, overwrite: true);
    }

    private static void DeleteUnlisted(string folder, DocsSection section)
    {
        var listed = section.Shots.Select(shot => shot.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(folder, "*.webp"))
        {
            var stem = Path.GetFileName(file);
            if (!listed.Contains(stem[..stem.IndexOf('.', StringComparison.Ordinal)]))
                File.Delete(file);
        }
    }
}
