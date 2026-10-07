using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Talesmith.Authoring;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Runtime.Serialization;

/// <summary>The definitions of every component that scenes, prefabs and the editor can use, found by type name or type.</summary>
/// <remarks>
/// Every type registered with <c>services.AddComponent&lt;T&gt;()</c> gets a <see cref="ReflectionComponentDefinition{T}"/>, unless an
/// <see cref="IComponentDefinition"/> for the type is registered as a singleton, which wins. Custom definitions need no registration of
/// their own. Type names follow <see cref="GetTypeName"/>; when two types claim one name, the first keeps it and the other is logged and
/// left out.
/// </remarks>
public sealed class ComponentRegistry
{
    private readonly Dictionary<string, IComponentDefinition> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, IComponentDefinition> _byType = new();
    private readonly List<IComponentDefinition> _definitions = [];

    public ComponentRegistry(IEnumerable<ComponentRegistration> registrations, IEnumerable<IComponentDefinition> definitions, ValueConverterRegistry converters,
        ILogger<ComponentRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(converters);
        var custom = new Dictionary<Type, IComponentDefinition>();
        foreach (var definition in definitions)
            custom[definition.ComponentType] = definition;

        foreach (var registration in registrations)
        {
            if (_byType.ContainsKey(registration.Type))
                continue;
            if (custom.Remove(registration.Type, out var definition))
            {
                Add(definition, logger);
                continue;
            }

            try
            {
                var type = typeof(ReflectionComponentDefinition<>).MakeGenericType(registration.Type);
                Add((IComponentDefinition)Activator.CreateInstance(type, converters, logger)!, logger);
            }
            catch (Exception ex) when (ex is ArgumentException or TargetInvocationException or NotSupportedException)
            {
                logger.ComponentDefinitionFailed(ex, registration.Type.FullName ?? registration.Type.Name);
            }
        }

        foreach (var definition in custom.Values)
            Add(definition, logger);
    }

    /// <summary>Every definition, in registration order.</summary>
    public IReadOnlyList<IComponentDefinition> Definitions => _definitions;

    public bool TryGet(string typeName, [NotNullWhen(true)] out IComponentDefinition? definition) => _byName.TryGetValue(typeName, out definition);

    public bool TryGet(Type type, [NotNullWhen(true)] out IComponentDefinition? definition) => _byType.TryGetValue(type, out definition);

    public IComponentDefinition? Find(string typeName) => _byName.GetValueOrDefault(typeName);

    public IComponentDefinition? Find(Type type) => _byType.GetValueOrDefault(type);

    /// <summary>The name a component type is saved under.</summary>
    /// <remarks>
    /// Types marked <see cref="ComponentAttribute"/> in assemblies whose name starts with "Talesmith." use their short name, such as
    /// "Sprite"; every other type uses its full name, such as "MyGame.Health", so plugins cannot collide with each other or the engine.
    /// </remarks>
    public static string GetTypeName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var isEngine = type.IsDefined(typeof(ComponentAttribute), false) &&
                       type.Assembly.GetName().Name?.StartsWith("Talesmith.", StringComparison.Ordinal) == true;
        return isEngine ? type.Name : type.FullName ?? type.Name;
    }

    private void Add(IComponentDefinition definition, ILogger logger)
    {
        if (!_byName.TryAdd(definition.TypeName, definition))
        {
            logger.ComponentNameTaken(definition.ComponentType.FullName ?? definition.TypeName, definition.TypeName, _byName[definition.TypeName].ComponentType.FullName ?? "");
            return;
        }

        _byType[definition.ComponentType] = definition;
        _definitions.Add(definition);
    }
}
