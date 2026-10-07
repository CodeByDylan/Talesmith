using System.Reflection;
using System.Runtime.Loader;

namespace Talesmith.Scripting;

/// <summary>A collectible load context for one compilation of a game's scripts.</summary>
/// <remarks>
/// Scripts may only use the engine, the framework and the game's plugins, so every reference resolves to an assembly that is already
/// loaded: the explicit references first, then the host's, then the plugins' load contexts. Types therefore keep one identity, and
/// unloading the context releases only the scripts.
/// </remarks>
internal sealed class ScriptLoadContext(string name, IEnumerable<Assembly> references) : AssemblyLoadContext($"scripts:{name}", isCollectible: true)
{
    private readonly Dictionary<string, Assembly> _references =
        references.DistinctBy(a => a.GetName().Name).ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is null)
            return null;
        if (_references.TryGetValue(name, out var reference))
            return reference;
        if (Default.Assemblies.Any(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)))
            return null;

        Assembly? found = null;
        foreach (var context in All)
        {
            if (context is ScriptLoadContext || ReferenceEquals(context, Default))
                continue;
            foreach (var assembly in context.Assemblies)
            {
                if (string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                    found = assembly;
            }
        }

        return found;
    }
}
