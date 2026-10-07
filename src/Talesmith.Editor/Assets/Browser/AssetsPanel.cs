using Avalonia.Controls;
using Avalonia.Layout;
using Talesmith.Editor.Panels;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Assets.Browser;

/// <summary>The Assets panel.</summary>
public sealed class AssetsPanel(AssetBrowserViewModel viewModel) : IEditorPanel
{
    public Control CreateContent() => new AssetBrowserView { DataContext = viewModel };

    public Control? CreateHeaderActions()
    {
        var health = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.Activity, Size = 14 }, Command = viewModel.ShowHealthCommand };
        ToolTip.SetTip(health, "Asset health");
        var rescan = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.Refresh, Size = 14 }, Command = viewModel.RescanCommand };
        ToolTip.SetTip(rescan, "Scan the asset folder again");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { health, rescan } };
    }
}
