using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Panels;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Hierarchy;

/// <summary>The Hierarchy panel: the open scene's entities as a tree, or the play session's world while playing.</summary>
public sealed class HierarchyPanel(HierarchyViewModel viewModel) : IEditorPanel
{
    private HierarchyView? _view;

    public Control CreateContent() => _view = new HierarchyView { DataContext = viewModel };

    public Control? CreateHeaderActions()
    {
        var collapse = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.ChevronsUp, Size = 14 } };
        ToolTip.SetTip(collapse, "Collapse all");
        collapse.Click += (_, _) =>
        {
            foreach (var row in viewModel.Tree.GetChildren(null))
                viewModel.Tree.SetExpandedRecursive(row.Id, false);
        };
        return collapse;
    }

    /// <summary>Focuses the filter box.</summary>
    public void FocusSearch() => _view?.FocusSearch();
}

public static class HierarchyServiceCollectionExtensions
{
    /// <summary>The Hierarchy panel with its tree, clipboard, entity templates and commands.</summary>
    public static IServiceCollection AddEditorHierarchy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<EntityIcons>();
        services.TryAddSingleton<EntityTemplates>();
        services.TryAddSingleton<EntityClipboard>();
        services.TryAddSingleton<SceneAssetDrop>();
        services.TryAddSingleton<HierarchyTree>();
        services.TryAddSingleton<HierarchyViewModel>();
        services.AddEditorCommands<HierarchyCommands>();
        services.AddEditorPanel<HierarchyPanel>(new EditorPanelInfo(PanelIds.Hierarchy, "Hierarchy", Icons.ListTree, DockLocation.Left) { CanClose = false });
        return services;
    }
}
