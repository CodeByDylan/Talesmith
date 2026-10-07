using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Talesmith.Authoring;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.Systems;

namespace Talesmith.Scripting;

/// <summary>Where a game's compiled scripts come from.</summary>
public sealed class ScriptingOptions
{
    /// <summary>Where builds put the compiled scripts, relative to the asset root.</summary>
    public const string DefaultAssemblyPath = "scripts/bin/" + ScriptAssembly.DefaultName + ".dll";

    /// <summary>The game's asset folder; with it, scripts load from <see cref="AssemblyPath"/> when <see cref="Assembly"/> is not set.</summary>
    public string? AssetRoot { get; set; }

    /// <summary>The compiled scripts' path relative to <see cref="AssetRoot"/>; a missing file means the game has no scripts.</summary>
    public string AssemblyPath { get; set; } = DefaultAssemblyPath;

    /// <summary>Scripts that are already loaded, such as the editor's latest compilation; wins over <see cref="AssemblyPath"/>.</summary>
    /// <remarks>Once the game is built, the scripts it uses; hot reload replaces them.</remarks>
    public ScriptAssembly? Assembly { get; set; }

    /// <summary>Why the scripts at <see cref="AssemblyPath"/> could not be loaded; the game logs it when it starts.</summary>
    public Exception? LoadError { get; internal set; }
}

/// <summary>Registers the scripting module.</summary>
public static class ScriptingServiceCollectionExtensions
{
    /// <summary>Adds scripts: the <see cref="ScriptComponent"/>, the systems that run scripts, hot reload, and the game's compiled scripts with their systems, components and scene listeners.</summary>
    /// <remarks>
    /// Call it after plugins are configured, since scripts may use plugin types. Calling it again does nothing: the compiled scripts are
    /// chosen once per game.
    /// </remarks>
    public static IServiceCollection AddTalesmithScripting(this IServiceCollection services, Action<ScriptingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(d => d.ServiceType == typeof(ScriptingOptions)))
            return services;

        var options = new ScriptingOptions();
        configure?.Invoke(options);
        if (options.Assembly is null && options.AssetRoot is { } root)
        {
            var path = Path.Combine(root, options.AssemblyPath);
            if (File.Exists(path))
            {
                try
                {
                    options.Assembly = ScriptAssembly.LoadFile(path);
                }
                catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
                {
                    options.LoadError = ex;
                }
            }
        }

        services.AddSingleton(options);
        services.TryAddSingleton<ScriptTypeRegistry>();
        services.TryAddSingleton<ScriptReloader>();
        services.TryAddScoped<ScriptRuntime>();
        services.AddSceneListener<ScriptSceneListener>();
        services.AddSingleton<IComponentDefinition, ScriptComponentDefinition>();
        services.AddScoped(sp => new FixedUpdateScriptsSystem(sp.GetRequiredService<ScriptRuntime>()));
        services.AddScoped(sp => new UpdateScriptsSystem(sp.GetRequiredService<ScriptRuntime>()));
        services.AddScoped(sp => new LateUpdateScriptsSystem(sp.GetRequiredService<ScriptRuntime>()));
        services.AddSystem<FixedUpdateScriptsSystem>();
        services.AddSystem<UpdateScriptsSystem>();
        services.AddSystem<LateUpdateScriptsSystem>();

        if (options.Assembly is { } scripts)
        {
            foreach (var system in scripts.SystemTypes)
                services.AddSystem(SystemDescriptor.For(system));
            foreach (var component in scripts.ComponentTypes)
                services.AddComponent(component);
            foreach (var listener in scripts.SceneListenerTypes)
                services.AddScoped(typeof(ISceneListener), listener);
        }

        return services;
    }

    /// <summary>Makes a script type that is not part of the game's compiled scripts, such as one from a plugin, available to scenes and the editor.</summary>
    public static IServiceCollection AddScript<T>(this IServiceCollection services) where T : Script, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new ScriptRegistration(typeof(T)));
        return services;
    }
}
