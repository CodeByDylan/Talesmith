using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Talesmith.Runtime.Serialization.Converters;

/// <summary>Saves enums by camelCase name; flags as names separated by commas.</summary>
internal sealed class EnumConverterFactory : IValueConverterFactory
{
    public bool CanConvert(Type type) => type.IsEnum;

    public IValueConverter Create(Type type, ValueConverterRegistry converters) =>
        (IValueConverter)Activator.CreateInstance(typeof(EnumConverter<>).MakeGenericType(type))!;
}

internal sealed class EnumConverter<T> : ValueConverter<T> where T : struct, Enum
{
    private static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), false);
    private readonly Dictionary<T, string> _names = new();
    private readonly Dictionary<string, T> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly (T Value, ulong Bits, string Name)[] _flags;

    public EnumConverter()
    {
        foreach (var value in Enum.GetValues<T>())
        {
            var name = JsonNames.ToJson(value.ToString());
            _names.TryAdd(value, name);
            _values.TryAdd(name, value);
        }

        _flags = _names.Select(p => (p.Key, Bits(p.Key), p.Value)).Where(f => f.Item2 != 0 && ulong.IsPow2(f.Item2)).ToArray();
    }

    public override PropertyKind Kind => PropertyKind.Enum;

    public override JsonNode Write(T value, ICaptureContext context)
    {
        if (_names.TryGetValue(value, out var name))
            return JsonValue.Create(name);
        if (IsFlags)
        {
            var bits = Bits(value);
            var parts = _flags.Where(f => (bits & f.Bits) == f.Bits).Select(f => f.Name).ToList();
            var covered = _flags.Where(f => (bits & f.Bits) == f.Bits).Aggregate(0UL, (all, f) => all | f.Bits);
            if (covered == bits)
                return JsonValue.Create(string.Join(", ", parts));
        }

        return JsonValue.Create(Convert.ToInt64(value, null));
    }

    public override T Read(JsonNode node, IInstantiationContext context)
    {
        if (node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            return (T)Enum.ToObject(typeof(T), JsonFormats.GetInteger(node));

        var text = JsonFormats.GetString(node);
        if (_values.TryGetValue(text, out var result))
            return result;
        if (IsFlags)
        {
            ulong bits = 0;
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                bits |= _values.TryGetValue(part, out var flag) ? Bits(flag) : throw new FormatException($"'{part}' is not a {typeof(T).Name}.");
            return (T)Enum.ToObject(typeof(T), bits);
        }

        return Enum.TryParse<T>(text, true, out result) ? result : throw new FormatException($"'{text}' is not a {typeof(T).Name}.");
    }

    public override PropertyDescriptor Describe(PropertyDescriptor property) =>
        property with { Kind = Kind, EnumNames = _names.Values.Distinct().ToArray(), IsFlags = IsFlags };

    private static ulong Bits(T value) =>
        Type.GetTypeCode(typeof(T)) == TypeCode.UInt64 ? Convert.ToUInt64(value, null) : unchecked((ulong)Convert.ToInt64(value, null));
}

/// <summary>Saves nullable values as the value or JSON null.</summary>
internal sealed class NullableConverterFactory : IValueConverterFactory
{
    public bool CanConvert(Type type) => Nullable.GetUnderlyingType(type) is not null;

    public IValueConverter Create(Type type, ValueConverterRegistry converters)
    {
        var underlying = Nullable.GetUnderlyingType(type)!;
        var inner = converters.Get(underlying);
        return (IValueConverter)Activator.CreateInstance(typeof(NullableConverter<>).MakeGenericType(underlying), inner)!;
    }
}

internal sealed class NullableConverter<T>(ValueConverter<T> inner) : ValueConverter<T?> where T : struct
{
    public override PropertyKind Kind => inner.Kind;

    public override JsonNode? Write(T? value, ICaptureContext context) => value is { } v ? inner.Write(v, context) : null;

    public override T? Read(JsonNode node, IInstantiationContext context) => inner.Read(node, context);

    public override PropertyDescriptor Describe(PropertyDescriptor property) =>
        inner.Describe(property with { ValueType = typeof(T) }) with { ValueType = typeof(T?), IsNullable = true };

    public override void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies) => inner.CollectDependencies(node, dependencies);
}

/// <summary>Saves arrays, lists, sets and immutable arrays as JSON arrays; sets of comparable values are written sorted so files do not churn.</summary>
internal sealed class CollectionConverterFactory : IValueConverterFactory
{
    private static readonly Type[] ListTypes =
    [
        typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(IEnumerable<>), typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>)
    ];

    private static readonly Type[] SetTypes = [typeof(HashSet<>), typeof(ISet<>), typeof(IReadOnlySet<>)];

    public bool CanConvert(Type type) => Classify(type) is not CollectionKind.None;

    public IValueConverter Create(Type type, ValueConverterRegistry converters)
    {
        var element = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
        var elementConverter = converters.Get(element);
        var method = typeof(CollectionConverterFactory).GetMethod(nameof(CreateTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IValueConverter)method.MakeGenericMethod(type, element).Invoke(null, [Classify(type), elementConverter])!;
    }

    private static ValueConverter<TCollection> CreateTyped<TCollection, TElement>(CollectionKind kind, ValueConverter<TElement> element)
    {
        var sorted = kind is CollectionKind.Set or CollectionKind.ImmutableSet && typeof(IComparable<TElement>).IsAssignableFrom(typeof(TElement));
        Func<List<TElement>, object> build = kind switch
        {
            CollectionKind.Array => items => items.ToArray(),
            CollectionKind.List => items => items,
            CollectionKind.ImmutableArray => items => items.ToImmutableArray(),
            CollectionKind.Set => items => new HashSet<TElement>(items),
            _ => items => items.ToImmutableHashSet()
        };
        return new SequenceConverter<TCollection, TElement>(element, items => (TCollection)build(items), sorted);
    }

    private static CollectionKind Classify(Type type)
    {
        if (type.IsArray)
            return type.GetArrayRank() == 1 ? CollectionKind.Array : CollectionKind.None;
        if (!type.IsGenericType)
            return CollectionKind.None;
        var definition = type.GetGenericTypeDefinition();
        if (ListTypes.Contains(definition))
            return CollectionKind.List;
        if (definition == typeof(ImmutableArray<>))
            return CollectionKind.ImmutableArray;
        if (SetTypes.Contains(definition))
            return CollectionKind.Set;
        return definition == typeof(ImmutableHashSet<>) || definition == typeof(IImmutableSet<>) ? CollectionKind.ImmutableSet : CollectionKind.None;
    }

    private enum CollectionKind
    {
        None,
        Array,
        List,
        ImmutableArray,
        Set,
        ImmutableSet
    }
}

internal sealed class SequenceConverter<TCollection, TElement>(ValueConverter<TElement> element, Func<List<TElement>, TCollection> build, bool sorted)
    : ValueConverter<TCollection>
{
    private static readonly bool IsImmutableArray = typeof(TCollection) == typeof(ImmutableArray<TElement>);

    public override PropertyKind Kind => PropertyKind.List;

    public override JsonNode? Write(TCollection value, ICaptureContext context)
    {
        IEnumerable<TElement>? items = IsImmutableArray
            ? ((ImmutableArray<TElement>)(object)value!).IsDefault ? [] : (ImmutableArray<TElement>)(object)value!
            : (IEnumerable<TElement>?)value;
        if (items is null)
            return null;
        if (sorted)
            items = items.Order();

        var array = new JsonArray();
        foreach (var item in items)
            array.Add(element.Write(item, context));
        return array;
    }

    public override TCollection Read(JsonNode node, IInstantiationContext context)
    {
        if (node is not JsonArray array)
            throw new FormatException($"Expected a list but found {JsonFormats.Describe(node)}.");
        var items = new List<TElement>(array.Count);
        foreach (var item in array)
            items.Add(element.ReadOrDefault(item, context));
        return build(items);
    }

    public override PropertyDescriptor Describe(PropertyDescriptor property) =>
        property with { Kind = Kind, Element = element.Describe(new PropertyDescriptor("item", "Item", PropertyKind.Object, typeof(TElement))) };

    public override void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies)
    {
        if (node is not JsonArray array)
            return;
        foreach (var item in array)
        {
            if (item is not null)
                element.CollectDependencies(item, dependencies);
        }
    }
}

/// <summary>Saves structs, classes and records as JSON objects of their public fields and settable properties.</summary>
internal sealed class ObjectConverterFactory : IValueConverterFactory
{
    public bool CanConvert(Type type) =>
        type is { IsAbstract: false, IsInterface: false, IsPointer: false, IsByRef: false, IsPrimitive: false, IsEnum: false, IsArray: false, ContainsGenericParameters: false }
        && !type.IsByRefLike
        && !typeof(Delegate).IsAssignableFrom(type)
        && type != typeof(object) && type != typeof(string)
        && type.Namespace?.StartsWith("System", StringComparison.Ordinal) != true;

    public IValueConverter Create(Type type, ValueConverterRegistry converters) =>
        (IValueConverter)Activator.CreateInstance(typeof(ObjectConverter<>).MakeGenericType(type), converters)!;
}

internal sealed class ObjectConverter<T>(ValueConverterRegistry converters) : ValueConverter<T>
{
    private readonly ObjectShape<T> _shape = new(converters);

    public override PropertyKind Kind => PropertyKind.Object;

    public override JsonNode? Write(T value, ICaptureContext context) => value is null ? null : _shape.Write(ref value, context);

    public override T Read(JsonNode node, IInstantiationContext context)
    {
        if (node is not JsonObject data)
            throw new FormatException($"Expected an object but found {JsonFormats.Describe(node)}.");
        var value = _shape.Create();
        _shape.Read(ref value, data, context);
        return value;
    }

    public override PropertyDescriptor Describe(PropertyDescriptor property) =>
        property with { Kind = Kind, Children = _shape.Properties, IsNullable = property.IsNullable };

    public override void CollectDependencies(JsonNode node, ICollection<AssetDependency> dependencies)
    {
        if (node is JsonObject data)
            _shape.CollectDependencies(data, dependencies);
    }
}
