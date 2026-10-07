using Talesmith.Editor.Assets.Browser;
using Talesmith.Editor.Assets.Creation;
using Talesmith.Editor.Assets.Health;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Plugins;
using Talesmith.UI;

namespace Talesmith.Editor.Assets;

/// <summary>The Assets menu's Create submenu, importing and Asset Health, also in the command palette.</summary>
public sealed class AssetCommands(AssetBrowserViewModel browser, AssetHealthLauncher health, EditorPluginGuard plugins) : IEditorCommandContributor
{
    private const string Category = "Assets";

    public void Contribute(CommandBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Add("assets.health", "Asset health…", Category, health.ShowAsync, icon: Icons.Activity,
            description: "Lists missing references, orphaned .meta files and unused assets.");
        builder.Add("assets.import", "Import files…", Category, () => browser.ImportCommand.ExecuteAsync(null), () => browser.IsReady, icon: Icons.Import,
            description: "Copies files into the folder the Assets panel shows.");
        var folder = builder.Add("assets.create.folder", "Create folder", Category, browser.CreateFolderAsync, () => browser.IsReady, icon: Icons.FolderPlus);
        builder.Menu(MenuPaths.Assets + "/Create", new MenuItemViewModel { Header = "Folder", Command = folder.Command, Icon = folder.Icon }, "folder");
        foreach (var factory in browser.Factories)
        {
            try
            {
                AddCreate(builder, factory);
            }
            catch (InvalidOperationException ex) when (factory is PluginAssetFactory fromPlugin)
            {
                plugins.Report(fromPlugin.Plugin, "add its Create menu entry", ex);
            }
        }

        builder.Menu(MenuPaths.Assets, "assets.import", "import");
        builder.Menu(MenuPaths.Assets, "assets.health", "health");
    }

    private void AddCreate(CommandBuilder builder, IAssetFactory factory)
    {
        var id = $"assets.create.{factory.Kind.Id}.{factory.Title.Replace(' ', '-').ToLowerInvariant()}";
        var command = builder.Add(id, factory.Submenu is null ? $"Create {factory.Title}" : $"Create {factory.Submenu}: {factory.Title}", Category,
            () => browser.CreateAsync(factory), () => browser.IsReady, icon: factory.Icon());
        builder.Menu(factory.Submenu is null ? MenuPaths.Assets + "/Create" : $"{MenuPaths.Assets}/Create/{factory.Submenu}",
            new MenuItemViewModel { Header = factory.Title, Command = command.Command, Icon = command.Icon }, factory.Group, factory.Order);
    }
}
