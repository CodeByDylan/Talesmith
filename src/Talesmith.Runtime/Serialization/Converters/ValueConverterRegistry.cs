using System.Diagnostics.CodeAnalysis;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Audio;

namespace Talesmith.Runtime.Serialization.Converters;

/// <summary>Finds the converter for a value type: registered converters first, then factories, then the built-in converters.</summary>
/// <remarks>
/// Built in are booleans, integers, floating-point numbers, strings, guids, enums (including flags), <c>Vector2</c>, <c>Color</c>,
/// <c>Rect2</c>, <c>Curve</c>, <c>Gradient</c>, nullable values, arrays, lists, sets, immutable arrays, nested objects and records,
/// <c>AssetGuid</c>, <c>Entity</c>, and references to texture, tile map, sound and music assets. Converters are created once per type.
/// </remarks>
public sealed class ValueConverterRegistry
{
    private readonly Dictionary<Type, IValueConverter> _converters = new();
    private readonly List<IValueConverterFactory> _factories;
    private readonly Dictionary<Type, IValueConverter?> _cache = new();
    private readonly Lock _lock = new();

    public ValueConverterRegistry(IEnumerable<IValueConverter> converters, IEnumerable<IValueConverterFactory> factories)
    {
        foreach (var converter in BuiltInConverters())
            _converters[converter.ValueType] = converter;
        foreach (var converter in converters)
            _converters[converter.ValueType] = converter;

        _factories = factories.Reverse().ToList();
        _factories.Add(new EnumConverterFactory());
        _factories.Add(new NullableConverterFactory());
        _factories.Add(new CollectionConverterFactory());
        _factories.Add(new ObjectConverterFactory());
    }

    /// <summary>A registry with only the built-in converters, for tools and tests.</summary>
    public static ValueConverterRegistry CreateDefault() => new([], []);

    public bool TryGet(Type type, [NotNullWhen(true)] out IValueConverter? converter)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_lock)
        {
            if (!_cache.TryGetValue(type, out converter))
            {
                converter = Create(type);
                _cache[type] = converter;
            }
        }

        return converter is not null;
    }

    /// <exception cref="NotSupportedException">No converter handles the type.</exception>
    public IValueConverter Get(Type type) =>
        TryGet(type, out var converter) ? converter : throw new NotSupportedException($"No value converter handles {type}. Register one with AddValueConverter.");

    /// <exception cref="NotSupportedException">No converter handles the type.</exception>
    public ValueConverter<T> Get<T>() => (ValueConverter<T>)Get(typeof(T));

    private IValueConverter? Create(Type type)
    {
        if (_converters.TryGetValue(type, out var converter))
            return converter;
        foreach (var factory in _factories)
        {
            if (!factory.CanConvert(type))
                continue;
            var created = factory.Create(type, this);
            if (!typeof(ValueConverter<>).MakeGenericType(type).IsInstanceOfType(created))
                throw new InvalidOperationException($"{factory.GetType().Name} created {created.GetType().Name} for {type}, which is not a ValueConverter<{type.Name}>.");
            return created;
        }

        return null;
    }

    private static IEnumerable<IValueConverter> BuiltInConverters() =>
    [
        new BooleanConverter(),
        new IntegerConverter<sbyte>(),
        new IntegerConverter<byte>(),
        new IntegerConverter<short>(),
        new IntegerConverter<ushort>(),
        new IntegerConverter<int>(),
        new IntegerConverter<uint>(),
        new IntegerConverter<long>(),
        new IntegerConverter<ulong>(),
        new SingleConverter(),
        new DoubleConverter(),
        new DecimalConverter(),
        new StringConverter(),
        new GuidConverter(),
        new Vector2Converter(),
        new ColorConverter(),
        new Rect2Converter(),
        new CurveConverter(),
        new GradientConverter(),
        new AssetGuidConverter(),
        new EntityConverter(),
        new AssetReferenceConverter<TextureAsset>(),
        new AssetReferenceConverter<TileMap>(),
        new AssetReferenceConverter<SoundClip>(),
        new AssetReferenceConverter<MusicTrack>()
    ];
}
