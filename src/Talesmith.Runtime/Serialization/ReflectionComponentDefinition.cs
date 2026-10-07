using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Authoring;
using Talesmith.Ecs;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Runtime.Serialization;

/// <summary>A component definition built from a type's public fields and settable properties and its authoring attributes.</summary>
/// <remarks>Properties missing from saved data, and values that cannot be read (which are logged), keep the type's defaults.</remarks>
public class ReflectionComponentDefinition<T> : IComponentDefinition
{
    private readonly ObjectShape<T> _shape;
    private readonly ILogger _logger;
    private readonly Action<MemberBinding<T>, Exception> _onReadError;

    public ReflectionComponentDefinition(ValueConverterRegistry converters, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(converters);
        _shape = new ObjectShape<T>(converters);
        _logger = logger ?? NullLogger.Instance;
        TypeName = ComponentRegistry.GetTypeName(typeof(T));
        Info = CreateInfo(typeof(T));
        _onReadError = (member, error) => _logger.ComponentValueInvalid(TypeName, member.Name, error.Message);
    }

    public string TypeName { get; }

    public Type ComponentType => typeof(T);

    public ComponentInfo Info { get; }

    public IReadOnlyList<PropertyDescriptor> Properties => _shape.Properties;

    /// <summary>Members that are not saved because no value converter handles their type.</summary>
    public IReadOnlyList<string> UnsupportedMembers => _shape.Unsupported;

    public JsonObject CreateDefault() => CaptureValue(_shape.Create(), NullCaptureContext.Instance);

    public IEnumerable<AssetDependency> GetDependencies(JsonObject data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var dependencies = new List<AssetDependency>();
        _shape.CollectDependencies(data, dependencies);
        return dependencies;
    }

    public void Apply(World world, Entity entity, JsonObject data, IInstantiationContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(data);
        var value = ReadValue(data, context);
        Store(world, entity, value);
        OnApplied(world, entity, data, context);
    }

    public JsonObject? Capture(World world, Entity entity, ICaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.IsAlive(entity) && TryLoad(world, entity, out var value) ? CaptureValue(value, context) : null;
    }

    public JsonObject? CaptureEffective(World world, Entity entity, ICaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.IsAlive(entity) && TryLoad(world, entity, out var value) ? _shape.Write(ref value, context) : null;
    }

    public virtual void Remove(World world, Entity entity)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.IsAlive(entity))
            world.Remove<T>(entity);
    }

    /// <summary>Creates a component from saved data without putting it on an entity.</summary>
    public T ReadValue(JsonObject data, IInstantiationContext context)
    {
        var value = _shape.Create();
        _shape.Read(ref value, data, context, _onReadError);
        OnRead(ref value, data, context);
        return value;
    }

    /// <summary>Writes a component's saved data.</summary>
    public JsonObject CaptureValue(T value, ICaptureContext context)
    {
        var data = _shape.Write(ref value, context);
        OnCaptured(value, data, context);
        return data;
    }

    /// <summary>Adjusts a component after its saved values were read, such as filling values that derive from others.</summary>
    protected virtual void OnRead(ref T component, JsonObject data, IInstantiationContext context)
    {
    }

    /// <summary>Called after the component was put on the entity, for work that needs the entity, such as creating related entities.</summary>
    protected virtual void OnApplied(World world, Entity entity, JsonObject data, IInstantiationContext context)
    {
    }

    /// <summary>Adjusts captured data, such as leaving out values that derive from others.</summary>
    protected virtual void OnCaptured(in T component, JsonObject data, ICaptureContext context)
    {
    }

    /// <summary>Puts the component on the entity.</summary>
    protected virtual void Store(World world, Entity entity, in T component) => world.Set(entity, component);

    /// <summary>Gets the entity's component, if it has one.</summary>
    protected virtual bool TryLoad(World world, Entity entity, out T component) => world.TryGet(entity, out component);

    private static ComponentInfo CreateInfo(Type type)
    {
        var attribute = type.GetCustomAttribute<ComponentAttribute>();
        return new ComponentInfo(
            attribute?.DisplayName ?? DisplayNames.FromIdentifier(type.Name),
            attribute?.Category ?? "General",
            attribute?.Description,
            attribute?.Icon,
            attribute?.Hidden ?? false);
    }
}

/// <summary>A capture context for values that are not on an entity, such as defaults; it knows no assets or entities.</summary>
internal sealed class NullCaptureContext : ICaptureContext
{
    public static NullCaptureContext Instance { get; } = new();

    public Assets.AssetGuid GetGuid(object asset) => default;

    public Guid GetEntityId(Entity entity) => Guid.Empty;
}
