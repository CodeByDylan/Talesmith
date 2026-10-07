using System.Globalization;
using Talesmith.Screenshots;
using Talesmith.Screenshots.Capture;
using Talesmith.Screenshots.Docs;

var output = default(string);
var docs = default(string);
var scale = 2.0;
var filters = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--output" or "-o" when i + 1 < args.Length:
            output = Path.GetFullPath(args[++i]);
            break;
        case "--scale" when i + 1 < args.Length:
            scale = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--docs" when i + 1 < args.Length:
            docs = Path.GetFullPath(args[++i]);
            break;
        case "--docs-list":
            return DocsExporter.List();
        case "--shortcuts" when i + 1 < args.Length:
            return ShortcutExporter.Run(Path.GetFullPath(args[i + 1]));
        case "--perf":
            ScreenshotSession.InitializeAvalonia();
            return Talesmith.Screenshots.Perf.StressBenchmark.Run(
                i + 1 < args.Length ? int.Parse(args[i + 1], CultureInfo.InvariantCulture) : 2000,
                i + 2 < args.Length ? int.Parse(args[i + 2], CultureInfo.InvariantCulture) : 1000,
                i + 3 < args.Length ? int.Parse(args[i + 3], CultureInfo.InvariantCulture) : 5000);
        case "--list":
            foreach (var scene in SceneCatalog.All)
                Console.WriteLine(scene.Name);
            return 0;
        default:
            filters.Add(args[i]);
            break;
    }
}

if (docs is not null)
    return DocsExporter.Run(docs, filters);

output ??= Path.Combine(Repository.Root, "artifacts", "screenshots");
var scenes = SceneCatalog.All
    .Where(s => filters.Count == 0 || filters.Exists(f => s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
    .ToList();
if (scenes.Count == 0)
{
    Console.Error.WriteLine("No scene matches. Use --list to see the scenes.");
    return 1;
}

ScreenshotSession.InitializeAvalonia();
var session = new ScreenshotSession(output, scale);
Console.WriteLine($"Writing screenshots to {output}");
foreach (var scene in scenes)
    session.Capture(scene);
Console.WriteLine($"{session.Count} screenshots in dark and light.");
return 0;
