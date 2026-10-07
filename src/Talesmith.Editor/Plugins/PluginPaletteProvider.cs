using Talesmith.Editor.CommandPalette;

namespace Talesmith.Editor.Plugins;

/// <summary>A plugin's command palette provider called through the <see cref="EditorPluginGuard"/>.</summary>
public sealed class PluginPaletteProvider(ICommandPaletteProvider inner, EditorPluginGuard guard) : ICommandPaletteProvider
{
    public char? Prefix { get; } = guard.Run(inner, "describe its command palette results", () => inner.Prefix, null);

    public string Category { get; } = guard.Run(inner, "describe its command palette results", () => inner.Category, "Plugin");

    public IEnumerable<PaletteItem> Search(string query, int limit) =>
        guard.Run(inner, "search the command palette", () => inner.Search(query, limit).Select(Guarded).ToList(), []);

    private PaletteItem Guarded(PaletteItem item) => item with { Execute = () => guard.Run(inner, $"run \"{item.Title}\"", item.Execute) };
}
