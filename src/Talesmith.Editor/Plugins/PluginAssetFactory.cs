using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Creation;
using Talesmith.Plugins;

namespace Talesmith.Editor.Plugins;

/// <summary>A plugin's Create menu entry called through the <see cref="EditorPluginGuard"/>, with its description read once.</summary>
public sealed class PluginAssetFactory : IAssetFactory
{
    private readonly IAssetFactory _inner;
    private readonly EditorPluginGuard _guard;

    private PluginAssetFactory(IAssetFactory inner, PluginInfo plugin, EditorPluginGuard guard)
    {
        _inner = inner;
        _guard = guard;
        Plugin = plugin;
        Title = inner.Title;
        Kind = inner.Kind;
        Submenu = inner.Submenu;
        Group = inner.Group;
        Order = inner.Order;
        AsksForName = inner.AsksForName;
    }

    /// <summary>The plugin the entry belongs to.</summary>
    public PluginInfo Plugin { get; }

    public string Title { get; }

    public AssetKind Kind { get; }

    public string? Submenu { get; }

    public string Group { get; }

    public int Order { get; }

    public bool AsksForName { get; }

    /// <summary>The factory itself when it is the editor's own, a guarded one for a plugin's, or null when the plugin's cannot describe itself.</summary>
    public static IAssetFactory? Isolate(IAssetFactory factory, EditorPluginGuard guard)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(guard);
        return guard.FindPlugin(factory) is not { } plugin
            ? factory
            : guard.Run(factory, "add its Create menu entry", () => new PluginAssetFactory(factory, plugin, guard), null);
    }

    public Task<AssetRecord?> CreateAsync(AssetCreationContext context) => _guard.RunAsync(_inner, $"create {Title}", () => _inner.CreateAsync(context), null);
}
