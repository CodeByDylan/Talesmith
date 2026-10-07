using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Talesmith.Authoring;
using Talesmith.Runtime.Scenes;
using Talesmith.Systems;

namespace Talesmith.Scripting;

/// <summary>A compiled assembly of game scripts and what it contains: script types, systems, components and scene listeners.</summary>
/// <remarks>
/// Besides <see cref="Script"/> classes, the assembly may declare plain ECS code: every concrete <see cref="ISystem"/>, every type marked
/// <see cref="ComponentAttribute"/> and every <see cref="ISceneListener"/> is registered with the game automatically. Assemblies loaded
/// from bytes or files live in their own collectible load context, so <see cref="Unload"/> can release them after hot reload.
/// </remarks>
public sealed class ScriptAssembly
{
    /// <summary>The assembly name the script compiler uses unless configured otherwise.</summary>
    public const string DefaultName = "Game.Scripts";

    private readonly AssemblyLoadContext? _context;

    private ScriptAssembly(Assembly assembly, AssemblyLoadContext? context)
    {
        Assembly = assembly;
        _context = context;
        Name = assembly.GetName().Name ?? DefaultName;
        var types = LoadableTypes(assembly);
        ScriptTypes = types.Where(ScriptTypeRegistry.IsScriptType).ToArray();
        SystemTypes = types.Where(t => typeof(ISystem).IsAssignableFrom(t) && IsConcrete(t)).ToArray();
        ComponentTypes = types.Where(t => t.IsDefined(typeof(ComponentAttribute), false) && !t.IsAbstract && !t.ContainsGenericParameters).ToArray();
        SceneListenerTypes = types.Where(t => typeof(ISceneListener).IsAssignableFrom(t) && IsConcrete(t)).ToArray();
    }

    public Assembly Assembly { get; }

    public string Name { get; }

    /// <summary>Whether the assembly lives in its own load context that <see cref="Unload"/> can release.</summary>
    public bool IsCollectible => _context is not null;

    public IReadOnlyList<Type> ScriptTypes { get; }

    public IReadOnlyList<Type> SystemTypes { get; }

    public IReadOnlyList<Type> ComponentTypes { get; }

    public IReadOnlyList<Type> SceneListenerTypes { get; }

    /// <summary>Whether the assembly declares systems, components or scene listeners, which the game registers when it is built and hot reload cannot replace.</summary>
    public bool DeclaresEngineTypes => SystemTypes.Count > 0 || ComponentTypes.Count > 0 || SceneListenerTypes.Count > 0;

    /// <summary>Loads compiled scripts from memory into a new collectible load context.</summary>
    /// <param name="symbols">The portable PDB, so exceptions show file names and line numbers and debuggers can step through scripts.</param>
    /// <param name="references">Assemblies the scripts reference that the host did not load in its default context, such as plugins loaded in memory.</param>
    /// <exception cref="BadImageFormatException">The image is not a valid assembly.</exception>
    public static ScriptAssembly Load(byte[] image, byte[]? symbols = null, IEnumerable<Assembly>? references = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var context = new ScriptLoadContext(Guid.NewGuid().ToString("N")[..8], references ?? []);
        using var assembly = new MemoryStream(image, writable: false);
        using var pdb = symbols is null ? null : new MemoryStream(symbols, writable: false);
        try
        {
            return new ScriptAssembly(context.LoadFromStream(assembly, pdb), context);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    /// <summary>Loads compiled scripts from a file, and the <c>.pdb</c> beside it when present, reading them into memory so the files stay free to be rebuilt.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public static ScriptAssembly LoadFile(string path, IEnumerable<Assembly>? references = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var symbols = Path.ChangeExtension(path, ".pdb");
        return Load(File.ReadAllBytes(path), File.Exists(symbols) ? File.ReadAllBytes(symbols) : null, references);
    }

    /// <summary>Uses an assembly that is already loaded, such as scripts compiled into the game itself; it cannot be unloaded.</summary>
    public static ScriptAssembly FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return new ScriptAssembly(assembly, null);
    }

    /// <summary>Starts releasing a collectible assembly; the returned reference dies once nothing uses the scripts any more.</summary>
    /// <remarks>Drop every instance, delegate and type of the scripts first, such as by disposing the games that used them.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public WeakReference Unload()
    {
        if (_context is null)
            return new WeakReference(null);
        var reference = new WeakReference(_context, trackResurrection: true);
        _context.Unload();
        return reference;
    }

    public override string ToString() => Name;

    private static bool IsConcrete(Type type) => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false };

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }
}
