using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Particles.Presets;
using Talesmith.Editor.Particles.Preview;
using Talesmith.Editor.Undo;
using Talesmith.UI;

namespace Talesmith.Editor.Particles;

/// <summary>The Particles panel.</summary>
public sealed class ParticleEditorPanel(ParticleEditorViewModel viewModel, IUndoService undo) : IEditorPanel
{
    public Control CreateContent()
    {
        var view = new ParticleEditorView { DataContext = viewModel };
        UndoValueEdits.Attach(view, undo);
        return view;
    }
}

public static class ParticleEditorServiceCollectionExtensions
{
    /// <summary>Adds the particle editor: the Particles panel, its isolated preview and the preset gallery.</summary>
    public static IServiceCollection AddEditorParticles(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ParticleSettingsCodec>();
        services.TryAddSingleton<ParticlePreviewRenderer>();
        services.TryAddSingleton<ParticlePresetGallery>();
        services.TryAddSingleton<ParticleEditorViewModel>();
        services.AddSingleton<ICloseGuard>(sp => sp.GetRequiredService<ParticleEditorViewModel>());
        services.AddEditorPanel<ParticleEditorPanel>(new EditorPanelInfo(PanelIds.Particles, "Particles", Icons.Sparkles, DockLocation.Bottom) { Order = 40 });
        return services;
    }
}
