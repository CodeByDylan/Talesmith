using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Editor.Commands;

namespace Talesmith.Editor.Prefabs;

public static class PrefabServiceCollectionExtensions
{
    /// <summary>Prefab documents, instance expansion, override-aware entity editing and the prefab commands.</summary>
    public static IServiceCollection AddEditorPrefabs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<PrefabLibrary>();
        services.TryAddSingleton<PrefabInstances>();
        services.TryAddSingleton<EntityDataService>();
        services.TryAddSingleton<PrefabWorkflow>();
        services.AddSingleton<IEditorCommandContributor>(sp => sp.GetRequiredService<PrefabWorkflow>());
        return services;
    }
}
