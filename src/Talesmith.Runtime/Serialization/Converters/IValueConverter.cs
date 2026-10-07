using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Talesmith.Runtime.Serialization.Converters;

/// <summary>Translates values of one type between runtime form and saved JSON, and describes them for the inspector.</summary>
/// <remarks>Derive from <see cref="ValueConverter{T}"/>; the non-generic members exist for tools that work with any type.</remarks>
public interface IValueConverter
{
    Type ValueType { get; }

    /// <summary>Completes a property's description with this type's editor kind and hints.</summary>
    /// <param name="property">The property with its name, label and value type; its <see cref="PropertyDescriptor.Kind"/> is a placeholder.</param>
    PropertyDescriptor Describe(PropertyDescriptor property);

    /// <summary>Adds the assets a saved value refers to.</summary>
    void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies);

    JsonNode? WriteObject(object? value, ICaptureContext context);

    /// <exception cref="FormatException">The JSON does not hold a value of this type.</exception>
    object? ReadObject(JsonNode? node, IInstantiationContext context);
}

/// <summary>The base of value converters: reads and writes one type without boxing.</summary>
public abstract class ValueConverter<T> : IValueConverter
{
    public Type ValueType => typeof(T);

    /// <summary>The editor kind of the value.</summary>
    public abstract PropertyKind Kind { get; }

    /// <summary>Writes a value; null writes JSON null.</summary>
    public abstract JsonNode? Write(T value, ICaptureContext context);

    /// <summary>Reads a value from JSON that is not null.</summary>
    /// <exception cref="FormatException">The JSON does not hold a value of this type.</exception>
    public abstract T Read(JsonNode node, IInstantiationContext context);

    /// <summary>Reads a value, or the default for JSON null.</summary>
    public T ReadOrDefault(JsonNode? node, IInstantiationContext context) => node is null ? default! : Read(node, context);

    public virtual PropertyDescriptor Describe(PropertyDescriptor property) => property with { Kind = Kind };

    public virtual void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies)
    {
    }

    JsonNode? IValueConverter.WriteObject(object? value, ICaptureContext context) => value is null ? null : Write((T)value, context);

    object? IValueConverter.ReadObject(JsonNode? node, IInstantiationContext context) => ReadOrDefault(node, context);
}

/// <summary>Creates converters for families of types, such as enums or lists.</summary>
public interface IValueConverterFactory
{
    bool CanConvert(Type type);

    /// <param name="converters">Resolves converters of nested types, such as list elements.</param>
    IValueConverter Create(Type type, ValueConverterRegistry converters);
}

/// <summary>Registers value converters with dependency injection.</summary>
public static class ValueConverterServiceCollectionExtensions
{
    /// <summary>Adds a converter; converters added later win over earlier ones and over the built-in converters for the same type.</summary>
    public static IServiceCollection AddValueConverter<T>(this IServiceCollection services) where T : class, IValueConverter
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValueConverter, T>());
        return services;
    }

    /// <summary>Adds a converter factory; factories added later are asked first.</summary>
    public static IServiceCollection AddValueConverterFactory<T>(this IServiceCollection services) where T : class, IValueConverterFactory
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValueConverterFactory, T>());
        return services;
    }
}
