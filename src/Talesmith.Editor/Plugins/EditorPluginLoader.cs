using System.Reflection;
using Talesmith.Plugins;

namespace Talesmith.Editor.Plugins;

/// <summary>An editor plugin found in a plugin's editor assembly, with the plugin it belongs to.</summary>
public sealed record LoadedEditorPlugin(PluginInfo Plugin, IEditorPlugin Instance);

/// <summary>Finds and creates the <see cref="IEditorPlugin"/>s of the loaded plugins' editor assemblies.</summary>
public static class EditorPluginLoader
{
    /// <summary>Creates every editor plugin; a plugin whose editor plugins cannot be created is reported to <paramref name="guard"/> and left
    /// out, so one broken plugin does not stop the editor.</summary>
    public static IReadOnlyList<LoadedEditorPlugin> Load(IEnumerable<PluginAssemblies> assemblies, EditorPluginGuard guard)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(guard);
        var plugins = new List<LoadedEditorPlugin>();
        foreach (var loaded in assemblies)
        {
            if (loaded.EditorAssembly is not { } assembly)
                continue;
            try
            {
                var created = new List<LoadedEditorPlugin>();
                foreach (var type in assembly.GetExportedTypes())
                {
                    if (type is { IsAbstract: false, IsInterface: false } && typeof(IEditorPlugin).IsAssignableFrom(type))
                        created.Add(new LoadedEditorPlugin(loaded.Plugin, (IEditorPlugin)Activator.CreateInstance(type)!));
                }

                plugins.AddRange(created);
            }
            catch (Exception ex) when (ex is TypeLoadException or ReflectionTypeLoadException or MissingMethodException or TargetInvocationException
                                           or FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                guard.Report(loaded.Plugin, "create its editor plugin", ex is TargetInvocationException { InnerException: { } inner } ? inner : ex);
            }
        }

        return plugins;
    }
}
