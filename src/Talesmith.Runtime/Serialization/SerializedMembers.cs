using System.Reflection;
using System.Text.Json.Nodes;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Runtime.Serialization;

/// <summary>The saved members of a non-component type, such as a script, handled with the same rules and converters as components.</summary>
/// <remarks>
/// Saved members are public fields that are not read-only, fields marked <see cref="Authoring.SerializeFieldAttribute"/> and public
/// properties with public getters and setters, except those marked <see cref="Authoring.TransientAttribute"/>.
/// </remarks>
public abstract class SerializedMembers
{
    private protected SerializedMembers()
    {
    }

    public abstract Type Type { get; }

    /// <summary>The saved members, in inspector order.</summary>
    public abstract IReadOnlyList<PropertyDescriptor> Properties { get; }

    /// <summary>The C# fields and properties behind <see cref="Properties"/>, in the same order.</summary>
    public abstract IReadOnlyList<MemberInfo> Members { get; }

    /// <summary>Members that are not saved because no value converter handles their type.</summary>
    public abstract IReadOnlyList<string> UnsupportedMembers { get; }

    /// <summary>Describes the saved members of <paramref name="type"/>.</summary>
    /// <param name="include">Leaves out members for which it returns false, in addition to the usual rules.</param>
    public static SerializedMembers Create(Type type, ValueConverterRegistry converters, Func<MemberInfo, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(converters);
        return (SerializedMembers)Activator.CreateInstance(typeof(SerializedMembers<>).MakeGenericType(type), converters, include)!;
    }

    /// <summary>Creates an instance with the type's defaults.</summary>
    public abstract object Create();

    /// <summary>Sets the members present in <paramref name="data"/> on <paramref name="target"/>; others keep their values.</summary>
    /// <param name="onError">Receives the JSON name of each member whose value could not be read, which keeps its value; without it the error is thrown.</param>
    /// <returns><paramref name="target"/> itself for classes; a new boxed copy for structs.</returns>
    public abstract object Read(object target, JsonObject data, IInstantiationContext context, Action<string, Exception>? onError = null);

    public abstract JsonObject Write(object source, ICaptureContext context);

    /// <summary>Adds the assets that saved data refers to.</summary>
    public abstract void CollectDependencies(JsonObject data, ICollection<AssetDependency> dependencies);
}

internal sealed class SerializedMembers<T>(ValueConverterRegistry converters, Func<MemberInfo, bool>? include) : SerializedMembers
{
    private readonly ObjectShape<T> _shape = new(converters, include);
    private MemberInfo[]? _members;

    public override Type Type => typeof(T);

    public override IReadOnlyList<PropertyDescriptor> Properties => _shape.Properties;

    public override IReadOnlyList<MemberInfo> Members => _members ??= Array.ConvertAll(_shape.Members, m => m.Member);

    public override IReadOnlyList<string> UnsupportedMembers => _shape.Unsupported;

    public override object Create() => _shape.Create()!;

    public override object Read(object target, JsonObject data, IInstantiationContext context, Action<string, Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(data);
        Action<MemberBinding<T>, Exception>? report = onError is null ? null : (member, error) => onError(member.Name, error);
        var value = (T)target;
        _shape.Read(ref value, data, context, report);
        return value!;
    }

    public override JsonObject Write(object source, ICaptureContext context)
    {
        ArgumentNullException.ThrowIfNull(source);
        var value = (T)source;
        return _shape.Write(ref value, context);
    }

    public override void CollectDependencies(JsonObject data, ICollection<AssetDependency> dependencies)
    {
        ArgumentNullException.ThrowIfNull(data);
        _shape.CollectDependencies(data, dependencies);
    }
}
