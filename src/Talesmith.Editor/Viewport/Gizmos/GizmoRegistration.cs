using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Viewport.Gizmos;

public static class GizmoRegistration
{
    /// <summary>The move, rotate, scale and rect tools, the gizmo layer and the built-in component gizmos.</summary>
    public static IServiceCollection AddEditorGizmos(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<GizmoLayer>();
        services.AddViewportTool<MoveTool>();
        services.AddViewportTool<RotateTool>();
        services.AddViewportTool<ScaleTool>();
        services.AddViewportTool<RectTool>();
        services.AddGizmoProvider<CameraGizmo>();
        services.AddGizmoProvider<LightGizmo>();
        services.AddGizmoProvider<ColliderGizmo>();
        services.AddGizmoProvider<ShadowCasterGizmo>();
        services.AddGizmoProvider<ParticleGizmo>();
        services.AddGizmoProvider<AudioGizmo>();
        return services;
    }
}
