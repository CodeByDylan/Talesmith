using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Plugins;

/// <summary>Loads plugin assemblies into their context, finds the <see cref="IPlugin"/> class and runs it.</summary>
internal static class PluginActivator
{
    /// <exception cref="PluginLoadException">The plugin's dependency manifest cannot be read.</exception>
    public static PluginLoadContext CreateContext(string pluginId, string assemblyPath, IEnumerable<Assembly> visible, PluginLoadOptions options)
    {
        try
        {
            return new PluginLoadContext(pluginId, assemblyPath, options.SharedAssemblies, visible, options.Collectible, options.LoadInMemory);
        }
        catch (InvalidOperationException ex)
        {
            throw new PluginLoadException($"its dependency manifest ({Path.GetFileNameWithoutExtension(assemblyPath)}.deps.json) could not be read: {ex.Message}", ex);
        }
    }

    /// <exception cref="PluginLoadException">The assembly cannot be loaded.</exception>
    public static Assembly LoadAssembly(string assemblyPath, Func<string, Assembly> load)
    {
        if (!File.Exists(assemblyPath))
            throw new PluginLoadException($"its assembly {Path.GetFileName(assemblyPath)} was not found in {Path.GetDirectoryName(assemblyPath)}.");

        try
        {
            return load(assemblyPath);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException or InvalidOperationException)
        {
            throw new PluginLoadException($"its assembly {Path.GetFileName(assemblyPath)} could not be loaded: {ex.Message}", ex);
        }
    }

    /// <summary>Finds the single public, non-abstract <see cref="IPlugin"/> class with a parameterless constructor.</summary>
    /// <exception cref="PluginLoadException">There is no such class, more than one, or its types cannot be loaded.</exception>
    public static ConstructorInfo FindPlugin(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or FileLoadException or TypeLoadException)
        {
            throw new PluginLoadException($"the types in {assembly.GetName().Name} could not be loaded, usually because a dependency is missing: {ex.Message}", ex);
        }

        var pluginTypes = types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t)).ToList();
        if (pluginTypes.Count == 0)
            throw new PluginLoadException($"{assembly.GetName().Name} has no public, non-abstract class implementing {nameof(IPlugin)}.");
        if (pluginTypes.Count > 1)
            throw new PluginLoadException($"{assembly.GetName().Name} has more than one {nameof(IPlugin)} class ({string.Join(", ", pluginTypes.Select(t => t.FullName))}); keep exactly one.");

        var type = pluginTypes[0];
        return type.GetConstructor(Type.EmptyTypes) ?? throw new PluginLoadException($"{type.FullName} needs a public parameterless constructor.");
    }

    /// <summary>Creates the plugin and lets it register into a copy of <paramref name="services"/>.</summary>
    /// <exception cref="PluginLoadException">The constructor or <see cref="IPlugin.Configure"/> threw, or <paramref name="accept"/> refused.</exception>
    public static void Configure(
        ConstructorInfo constructor,
        PluginInfo info,
        IPluginSettings settings,
        IServiceCollection services,
        Func<IReadOnlyList<ServiceDescriptor>, string?> accept)
    {
        IPlugin plugin;
        try
        {
            plugin = (IPlugin)constructor.Invoke(null);
        }
        catch (TargetInvocationException ex)
        {
            var cause = ex.InnerException ?? ex;
            throw new PluginLoadException($"the constructor of {constructor.DeclaringType!.FullName} threw {cause.GetType().Name}: {cause.Message}", cause);
        }

        // Plugins configure a copy so a failure part-way through leaves no registrations behind.
        IServiceCollection staging = new ServiceCollection();
        foreach (var descriptor in services)
            staging.Add(descriptor);

        try
        {
            plugin.Configure(new PluginBuilder(staging, info, settings));
        }
        catch (Exception ex)
        {
            throw new PluginLoadException($"{plugin.GetType().FullName}.{nameof(IPlugin.Configure)} threw {ex.GetType().Name}: {ex.Message}", ex);
        }

        var existing = new HashSet<ServiceDescriptor>(services, ReferenceEqualityComparer.Instance);
        if (accept([.. staging.Where(d => !existing.Contains(d))]) is { } refusal)
            throw new PluginLoadException(refusal);

        services.Clear();
        foreach (var descriptor in staging)
            services.Add(descriptor);
    }
}

