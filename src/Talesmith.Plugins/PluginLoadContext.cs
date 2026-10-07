using System.Reflection;
using System.Runtime.Loader;

namespace Talesmith.Plugins;

/// <summary>Loads one plugin and its private dependencies in isolation, sharing contract assemblies with the host.</summary>
/// <remarks>
/// Assemblies resolve in this order: shared assemblies (names equal to, or starting with "name." for, an entry of
/// <see cref="DefaultSharedAssemblies"/> or the extra shared names) come from the host so types such as <see cref="IPlugin"/> keep a
/// single identity; the assemblies of the plugins this plugin depends on come from their own contexts; everything else, including native
/// libraries, resolves through the <c>.deps.json</c> of the plugin's runtime assembly and then of its editor assembly, so plugins can use
/// package versions that differ from the host's.
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    /// <summary>Assembly names (and name prefixes) that are always shared with the host.</summary>
    public static IReadOnlyList<string> DefaultSharedAssemblies { get; } =
        ["Talesmith", "Microsoft.Extensions", "Avalonia", "SkiaSharp", "CommunityToolkit", "System", "mscorlib", "netstandard"];

    private readonly List<AssemblyDependencyResolver> _resolvers;
    private readonly string[] _shared;
    private readonly Dictionary<string, Assembly> _pluginDependencies;
    private readonly bool _loadInMemory;

    /// <param name="pluginId">The id of the plugin this context belongs to.</param>
    /// <param name="entryAssemblyPath">The plugin's entry assembly; its <c>.deps.json</c> beside it drives resolution.</param>
    /// <param name="sharedAssemblies">Extra assembly names (or prefixes) to share with the host.</param>
    /// <param name="pluginDependencies">Assemblies of the plugins this plugin depends on, directly or indirectly.</param>
    /// <param name="isCollectible">Whether <see cref="AssemblyLoadContext.Unload"/> can release the plugin.</param>
    /// <param name="loadInMemory">Reads managed assemblies into memory so their files stay free to be rebuilt while loaded.</param>
    /// <exception cref="InvalidOperationException">The plugin's <c>.deps.json</c> cannot be read.</exception>
    public PluginLoadContext(
        string pluginId,
        string entryAssemblyPath,
        IEnumerable<string> sharedAssemblies,
        IEnumerable<Assembly> pluginDependencies,
        bool isCollectible,
        bool loadInMemory = false)
        : base($"plugin:{pluginId}", isCollectible)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        PluginId = pluginId;
        _resolvers = [new AssemblyDependencyResolver(entryAssemblyPath)];
        _shared = [.. DefaultSharedAssemblies, .. sharedAssemblies];
        _pluginDependencies = pluginDependencies.DistinctBy(a => a.GetName().Name).ToDictionary(a => a.GetName().Name!, StringComparer.OrdinalIgnoreCase);
        _loadInMemory = loadInMemory;
    }

    public string PluginId { get; }

    /// <summary>The plugin that owns <paramref name="assembly"/>, or null for host and engine assemblies.</summary>
    public static string? FindPlugin(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return GetLoadContext(assembly) is PluginLoadContext context ? context.PluginId : null;
    }

    /// <summary>Whether an assembly of this name is taken from the host instead of the plugin folder.</summary>
    public bool IsShared(string assemblyName)
    {
        foreach (var shared in _shared)
        {
            if (assemblyName.StartsWith(shared, StringComparison.OrdinalIgnoreCase) &&
                (assemblyName.Length == shared.Length || assemblyName[shared.Length] == '.'))
                return true;
        }

        return false;
    }

    /// <summary>Loads a plugin assembly by path, honoring the in-memory option.</summary>
    public Assembly LoadPluginAssembly(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _loadInMemory ? LoadIntoMemory(path) : LoadFromAssemblyPath(path);
    }

    /// <summary>Loads the plugin's editor assembly, resolving its private dependencies through its own <c>.deps.json</c> as well.</summary>
    /// <exception cref="InvalidOperationException">The editor assembly's <c>.deps.json</c> cannot be read.</exception>
    public Assembly LoadEditorAssembly(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        _resolvers.Add(new AssemblyDependencyResolver(path));
        return LoadPluginAssembly(path);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (name is not null && IsShared(name))
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
                // The host does not ship this shared assembly, so the plugin's own copy is the only one.
            }
        }

        if (name is not null && _pluginDependencies.TryGetValue(name, out var dependency))
            return dependency;

        foreach (var resolver in _resolvers)
        {
            if (resolver.ResolveAssemblyToPath(assemblyName) is { } path)
                return LoadPluginAssembly(path);
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        foreach (var resolver in _resolvers)
        {
            if (resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is { } path)
                return LoadUnmanagedDllFromPath(path);
        }

        return IntPtr.Zero;
    }

    private Assembly LoadIntoMemory(string path)
    {
        using var assembly = new MemoryStream(File.ReadAllBytes(path));
        var symbolsPath = Path.ChangeExtension(path, ".pdb");
        if (!File.Exists(symbolsPath))
            return LoadFromStream(assembly);
        using var symbols = new MemoryStream(File.ReadAllBytes(symbolsPath));
        return LoadFromStream(assembly, symbols);
    }
}
