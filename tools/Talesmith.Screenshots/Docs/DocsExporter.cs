using System.Diagnostics;
using Talesmith.Screenshots.Capture;

namespace Talesmith.Screenshots.Docs;

/// <summary>Renders the documentation site's screenshots as WebP into <c>{images}/screenshots/{section}</c>, with a manifest per section and the social card.</summary>
internal static class DocsExporter
{
    private const double Scale = 2;

    /// <param name="images">The site's image folder, website/static/img.</param>
    /// <param name="filters">Parts of full names, such as <c>guide/</c> or <c>project-hub</c>; empty renders every screenshot.</param>
    public static int Run(string images, IReadOnlyList<string> filters)
    {
        var shots = DocsCatalog.Sections
            .SelectMany(section => section.Shots.Select(shot => (Name: DocsCatalog.FullName(section, shot), Section: section, Shot: shot)))
            .Where(s => filters.Count == 0 || filters.Any(f => s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (shots.Count == 0)
        {
            Console.Error.WriteLine("No screenshot matches. Use --docs-list to see them.");
            return 1;
        }

        var folder = Path.Combine(images, "screenshots");
        ScreenshotSession.InitializeAvalonia();
        var session = new ScreenshotSession(folder, Scale, ImageFormat.Webp);
        var clock = Stopwatch.StartNew();
        Console.WriteLine($"Writing {shots.Count} screenshots to {folder}");
        var captured = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, section, shot) in shots)
        {
            DocsManifest.Record(Path.Combine(folder, section.Id), section, shot, session.Capture(shot.Scene, name));
            captured.Add(name);
        }

        var missing = DocsCatalog.Sections.SelectMany(section => DocsManifest.Prune(Path.Combine(folder, section.Id), section)).ToList();
        if (captured.Contains($"home/{HomeShots.Hero}"))
            SocialCard.Render(Path.Combine(folder, "home", $"{HomeShots.Hero}.dark.webp"), Path.Combine(images, "social-card.png"));

        Console.WriteLine($"{session.Count} screenshots in dark and light in {clock.Elapsed.TotalSeconds:0} s.");
        foreach (var name in missing)
            Console.WriteLine($"  Not captured yet: {name}");
        return 0;
    }

    /// <summary>Prints every screenshot's full name.</summary>
    public static int List()
    {
        foreach (var section in DocsCatalog.Sections)
        {
            foreach (var shot in section.Shots)
                Console.WriteLine(DocsCatalog.FullName(section, shot));
        }

        return 0;
    }
}
