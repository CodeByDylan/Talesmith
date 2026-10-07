using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Talesmith.Editor.Dialogs;
using Talesmith.Screenshots.Capture;
using Talesmith.Screenshots.Scenes;
using Talesmith.UI.Services;

namespace Talesmith.Screenshots.Docs;

/// <summary>Writes the editor's shortcut cheat sheet as JSON for the website, so the documented shortcuts are the registered ones.</summary>
internal static partial class ShortcutExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record Shortcut(string Action, IReadOnlyList<string> Keys);

    private sealed record Group(string Id, string Title, IReadOnlyList<Shortcut> Shortcuts);

    public static int Run(string path)
    {
        ScreenshotSession.InitializeAvalonia();
        var editor = EditorFixture.Open(EditorFixture.Platformer);
        var window = new Window { Content = editor.Root };
        RenderLoop.Host(window, 1280, 800, 1);
        editor.Attach(window);

        var sheet = new ShortcutsDialogViewModel(editor.Get<IDialogService>(), editor.Shell.Commands);
        var groups = sheet.Groups
            .Select(g => new Group(Slug(g.Category), g.Category, [.. g.Entries
                .GroupBy(e => e.Title)
                .Select(e => new Shortcut(e.Key, [.. e.Select(entry => entry.Keys).Distinct()]))]))
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using (var stream = File.Create(path))
            JsonSerializer.Serialize(stream, groups, Options);
        Console.WriteLine($"Wrote {groups.Sum(g => g.Shortcuts.Count)} shortcuts in {groups.Count} groups to {path}");
        window.Close();
        return 0;
    }

    private static string Slug(string text) => NonAlphanumeric().Replace(text.ToLowerInvariant(), "-").Trim('-');

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
