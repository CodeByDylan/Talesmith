using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Hierarchy;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.Panels;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector;

/// <summary>The Inspector panel.</summary>
public sealed class InspectorPanel(InspectorServices services) : IEditorPanel
{
    private InspectorView? _view;

    public Control CreateContent() => _view = new InspectorView(services);

    public Control? CreateHeaderActions()
    {
        var scene = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.Clapperboard, Size = 14 } };
        ToolTip.SetTip(scene, "Scene settings");
        scene.Click += (_, _) => _view?.ShowSceneSettings();
        return scene;
    }
}

public static class InspectorServiceCollectionExtensions
{
    /// <summary>The Inspector panel, the built-in property editors for every <c>PropertyKind</c> and live play-mode inspection.</summary>
    public static IServiceCollection AddEditorInspector(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<LiveSelection>();
        services.TryAddSingleton<PropertyEditorFactory>();
        services.TryAddSingleton(sp => new InspectorServices(
            sp.GetRequiredService<Documents.ISceneDocumentService>(),
            sp.GetRequiredService<ISelectionService>(),
            sp.GetRequiredService<IUndoService>(),
            sp.GetRequiredService<IProjectService>(),
            sp.GetRequiredService<IPlayModeService>(),
            sp.GetRequiredService<LiveSelection>(),
            sp.GetRequiredService<EntityDataService>(),
            sp.GetRequiredService<PrefabInstances>(),
            sp.GetRequiredService<PrefabWorkflow>(),
            sp.GetRequiredService<PrefabLibrary>(),
            sp.GetRequiredService<EntityIcons>(),
            sp.GetRequiredService<PropertyEditorFactory>(),
            sp.GetRequiredService<ProjectState>(),
            sp.GetService<Assets.Inspectors.AssetInspectorViewModel>(),
            sp.GetService<Scripting.IScriptService>(),
            sp.GetService<Viewport.IEditWorld>()));
        services.AddPropertyEditor<BooleanEditorProvider>();
        services.AddPropertyEditor<NumberEditorProvider>();
        services.AddPropertyEditor<StringEditorProvider>();
        services.AddPropertyEditor<EnumEditorProvider>();
        services.AddPropertyEditor<VectorEditorProvider>();
        services.AddPropertyEditor<ColorEditorProvider>();
        services.AddPropertyEditor<CurveEditorProvider>();
        services.AddPropertyEditor<AssetEditorProvider>();
        services.AddPropertyEditor<EntityEditorProvider>();
        services.AddPropertyEditor<ObjectEditorProvider>();
        services.AddPropertyEditor<ListEditorProvider>();
        services.TryAddSingleton<LayerNames>();
        services.AddPropertyEditor<LayerEditorProvider>();
        services.AddEditorPanel<InspectorPanel>(new EditorPanelInfo(PanelIds.Inspector, "Inspector", Icons.Sliders, DockLocation.Right) { Shortcut = "Ctrl+Shift+I" });
        return services;
    }
}
