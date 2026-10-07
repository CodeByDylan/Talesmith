using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Scripting;

/// <summary>Registers a script type that ships with the engine or a plugin rather than in the game's compiled scripts.</summary>
public sealed record ScriptRegistration(Type Type);

/// <summary>The script types a game knows: those registered with <c>services.AddScript&lt;T&gt;()</c> and those of the compiled scripts.</summary>
/// <remarks>
/// The editor uses it to list scripts in the Add Script menu and to show each script's fields with <see cref="Describe"/>, which returns
/// the same <see cref="PropertyDescriptor"/>s components use. Hot reload replaces the compiled scripts' types and raises
/// <see cref="Changed"/>. Script types save under their full name.
/// </remarks>
public sealed class ScriptTypeRegistry
{
    private readonly Lock _lock = new();
    private readonly Type[] _registered;
    private readonly IValueConverter[] _converters;
    private readonly IValueConverterFactory[] _factories;
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private Dictionary<string, ScriptTypeInfo> _byName = new(StringComparer.Ordinal);
    private Dictionary<Type, ScriptTypeInfo> _byType = new();
    private List<ScriptTypeInfo> _types = [];
    private ValueConverterRegistry _valueConverters = null!;

    public ScriptTypeRegistry(IEnumerable<ScriptRegistration> registrations, IEnumerable<IValueConverter> converters, IEnumerable<IValueConverterFactory> factories,
        ScriptingOptions options, ILogger<ScriptTypeRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _registered = registrations.Select(r => r.Type).Distinct().ToArray();
        _converters = converters.ToArray();
        _factories = factories.ToArray();
        if (options.LoadError is { } error)
            logger.ScriptAssemblyFailed(error, options.AssemblyPath);
        Rebuild(options.Assembly);
        if (options.Assembly is { } assembly)
            logger.ScriptAssemblyLoaded(assembly.ScriptTypes.Count, assembly.SystemTypes.Count, assembly.ComponentTypes.Count, assembly.Name);
    }

    /// <summary>Every known script type: registered ones first, then those of <see cref="Assembly"/>, each group by full name.</summary>
    public IReadOnlyList<ScriptTypeInfo> Types => _types;

    /// <summary>The game's compiled scripts, or null when it has none.</summary>
    public ScriptAssembly? Assembly { get; private set; }

    /// <summary>Script type names found in scenes or prefabs that no known type has, so the editor can report them.</summary>
    public IReadOnlyCollection<string> MissingTypes
    {
        get
        {
            lock (_lock)
                return _missing.ToArray();
        }
    }

    /// <summary>Raised after hot reload replaced the compiled script types.</summary>
    public event EventHandler? Changed;

    public ScriptTypeInfo? Find(string typeName) => _byName.GetValueOrDefault(typeName);

    public ScriptTypeInfo? Find(Type type) => _byType.GetValueOrDefault(type);

    public bool TryFind(string typeName, [NotNullWhen(true)] out ScriptTypeInfo? info) => _byName.TryGetValue(typeName, out info);

    /// <summary>The saved fields of a script type, in inspector order.</summary>
    /// <exception cref="ArgumentException">The type does not derive from <see cref="Script"/>.</exception>
    public IReadOnlyList<PropertyDescriptor> Describe(Type scriptType) => GetOrAdd(scriptType).Properties;

    /// <summary>Gets the information of a script type, adding types created in code that were never registered.</summary>
    /// <exception cref="ArgumentException">The type does not derive from <see cref="Script"/> or is abstract.</exception>
    public ScriptTypeInfo GetOrAdd(Type scriptType)
    {
        ArgumentNullException.ThrowIfNull(scriptType);
        if (_byType.TryGetValue(scriptType, out var info))
            return info;
        if (!IsScriptType(scriptType))
            throw new ArgumentException($"{scriptType.FullName} is not a concrete type deriving from {nameof(Script)}.", nameof(scriptType));

        lock (_lock)
        {
            if (_byType.TryGetValue(scriptType, out info))
                return info;
            info = new ScriptTypeInfo(scriptType, _types.Count, _valueConverters);
            _byType = new Dictionary<Type, ScriptTypeInfo>(_byType) { [scriptType] = info };
            if (!_byName.ContainsKey(info.TypeName))
                _byName = new Dictionary<string, ScriptTypeInfo>(_byName, StringComparer.Ordinal) { [info.TypeName] = info };
            _types = [.. _types, info];
            return info;
        }
    }

    internal static bool IsScriptType(Type type) =>
        typeof(Script).IsAssignableFrom(type) && type is { IsAbstract: false, IsGenericTypeDefinition: false } && type.GetConstructor(Type.EmptyTypes) is not null;

    internal void ReportMissing(string typeName)
    {
        lock (_lock)
            _missing.Add(typeName);
    }

    /// <summary>Describes the script types of a new compilation without using them yet, so hot reload can compare them first.</summary>
    internal Generation Prepare(ScriptAssembly? assembly)
    {
        var converters = CreateConverters();
        var types = new List<ScriptTypeInfo>();
        var byName = new Dictionary<string, ScriptTypeInfo>(StringComparer.Ordinal);
        var byType = new Dictionary<Type, ScriptTypeInfo>();
        var candidates = _registered.Where(IsScriptType).OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Concat(assembly?.ScriptTypes.OrderBy(t => t.FullName, StringComparer.Ordinal) ?? Enumerable.Empty<Type>());
        foreach (var type in candidates)
        {
            if (byType.ContainsKey(type))
                continue;
            var info = new ScriptTypeInfo(type, types.Count, converters);
            if (!byName.TryAdd(info.TypeName, info))
                continue;
            byType[type] = info;
            types.Add(info);
        }

        return new Generation(assembly, converters, types, byName, byType);
    }

    /// <summary>Switches to a prepared compilation; types of the previous one are forgotten.</summary>
    internal void Commit(Generation generation)
    {
        Use(generation);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Rebuild(ScriptAssembly? assembly) => Use(Prepare(assembly));

    private void Use(Generation generation)
    {
        lock (_lock)
        {
            _valueConverters = generation.Converters;
            _types = generation.Types;
            _byName = generation.ByName;
            _byType = generation.ByType;
            Assembly = generation.Assembly;
            _missing.RemoveWhere(generation.ByName.ContainsKey);
        }
    }

    internal sealed record Generation(ScriptAssembly? Assembly, ValueConverterRegistry Converters, List<ScriptTypeInfo> Types,
        Dictionary<string, ScriptTypeInfo> ByName, Dictionary<Type, ScriptTypeInfo> ByType);

    /// <summary>A converter registry per generation of script types, so converters cached for old script types are released with them.</summary>
    private ValueConverterRegistry CreateConverters() => new(_converters, _factories);
}
