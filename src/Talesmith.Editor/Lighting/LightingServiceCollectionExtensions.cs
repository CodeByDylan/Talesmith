using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Undo;
using Talesmith.UI;

namespace Talesmith.Editor.Lighting;

/// <summary>The Lighting panel.</summary>
public sealed class LightingPanel(LightingPanelViewModel viewModel, IUndoService undo) : IEditorPanel
{
    public Control CreateContent()
    {
        var view = new LightingView { DataContext = viewModel };
        UndoValueEdits.Attach(view, undo);
        return view;
    }
}

public static class LightingServiceCollectionExtensions
{
    /// <summary>Adds the Lighting panel and the viewport's edit-time lighting previews.</summary>
    public static IServiceCollection AddEditorLighting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<LightingPreviewService>();
        services.TryAddSingleton<LightingPanelViewModel>();
        services.AddEditorPanel<LightingPanel>(new EditorPanelInfo(PanelIds.Lighting, "Lighting", Icons.Lightbulb, DockLocation.Bottom) { Order = 50 });
        return services;
    }
}
