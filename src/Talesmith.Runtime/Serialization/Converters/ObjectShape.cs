using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Talesmith.Authoring;

namespace Talesmith.Runtime.Serialization.Converters;

internal delegate void ReadMember<T>(ref T target, JsonNode? node, IInstantiationContext context);

internal delegate JsonNode? WriteMember<T>(ref T source, ICaptureContext context);

/// <summary>A saved field or property of a type, with accessors compiled once so reading and writing it neither boxes nor reflects.</summary>
internal sealed class MemberBinding<T>(string name, MemberInfo member, IValueConverter converter, PropertyDescriptor descriptor, ReadMember<T> read, WriteMember<T> write)
{
    /// <summary>The key in saved JSON.</summary>
    public string Name { get; } = name;

    public MemberInfo Member { get; } = member;

    public IValueConverter Converter { get; } = converter;

    public PropertyDescriptor Descriptor { get; set; } = descriptor;

    public ReadMember<T> Read { get; } = read;

    public WriteMember<T> Write { get; } = write;
}

/// <summary>How a struct, class or record is saved: its constructor and the public fields and settable properties that are not transient.</summary>
internal sealed class ObjectShape<T>
{
    [ThreadStatic]
    private static bool _describing;

    private readonly ValueConverterRegistry _converters;
    private readonly Func<MemberInfo, bool>? _include;
    private MemberBinding<T>[]? _members;
    private IReadOnlyList<PropertyDescriptor>? _properties;
    private IReadOnlyList<string>? _unsupported;

    /// <param name="include">Leaves out saved members for which it returns false.</param>
    public ObjectShape(ValueConverterRegistry converters, Func<MemberInfo, bool>? include = null)
    {
        _converters = converters;
        _include = include;
        Create = BuildFactory();
    }

    /// <summary>Creates an instance with the type's defaults: its parameterless constructor, or its primary constructor with default arguments.</summary>
    public Func<T> Create { get; }

    public MemberBinding<T>[] Members => _members ?? Bind();

    /// <summary>Members that are skipped because no converter handles their type.</summary>
    public IReadOnlyList<string> Unsupported
    {
        get
        {
            _ = Members;
            return _unsupported!;
        }
    }

    public IReadOnlyList<PropertyDescriptor> Properties
    {
        get
        {
            if (_properties is not null)
                return _properties;
            if (_describing)
                return [];
            _describing = true;
            try
            {
                return _properties = Members.Select(m => m.Descriptor).ToArray();
            }
            finally
            {
                _describing = false;
            }
        }
    }

    /// <summary>Sets the members present in <paramref name="data"/>; others keep their current values.</summary>
    /// <param name="onError">Receives members whose values could not be read, which keep their values; without it the error is thrown.</param>
    public void Read(ref T target, JsonObject data, IInstantiationContext context, Action<MemberBinding<T>, Exception>? onError = null)
    {
        foreach (var member in Members)
        {
            if (!data.TryGetPropertyValue(member.Name, out var node))
                continue;
            try
            {
                member.Read(ref target, node, context);
            }
            catch (Exception ex) when (onError is not null && IsDataError(ex))
            {
                onError(member, ex);
            }
        }
    }

    public JsonObject Write(ref T source, ICaptureContext context)
    {
        var data = new JsonObject();
        foreach (var member in Members)
            data.Add(member.Name, member.Write(ref source, context));
        return data;
    }

    public void CollectDependencies(JsonObject data, ICollection<AssetDependency> dependencies)
    {
        foreach (var member in Members)
        {
            if (data.TryGetPropertyValue(member.Name, out var node) && node is not null)
            {
                try
                {
                    member.Converter.CollectDependencies(node, dependencies);
                }
                catch (Exception ex) when (IsDataError(ex))
                {
                }
            }
        }
    }

    public MemberBinding<T>? Find(string name) => Array.Find(Members, m => m.Name == name);

    internal static bool IsDataError(Exception ex) =>
        ex is FormatException or InvalidOperationException or InvalidCastException or OverflowException or JsonException or ArgumentException;

    private MemberBinding<T>[] Bind()
    {
        var members = new List<MemberBinding<T>>();
        var unsupported = new List<string>();
        var nullability = new NullabilityInfoContext();
        foreach (var member in SavedMembers(typeof(T)))
        {
            if (_include is not null && !_include(member))
                continue;
            var memberType = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
            if (!_converters.TryGet(memberType, out var converter))
            {
                unsupported.Add(member.Name);
                continue;
            }

            var name = JsonName(member);
            var descriptor = Describe(member, name, memberType, converter, nullability);
            members.Add(new MemberBinding<T>(name, member, converter, descriptor, CompileRead(member, memberType, converter), CompileWrite(member, memberType, converter)));
        }

        foreach (var binding in members)
        {
            if (binding.Member.GetCustomAttribute<TextureItemAttribute>() is { } item &&
                members.Find(m => m.Member.Name == item.TextureMember) is { } texture)
                binding.Descriptor = binding.Descriptor with { TextureItems = new TextureItemSource(texture.Name, item.Kind) };
        }

        _unsupported = unsupported;
        return _members = members.ToArray();
    }

    private static PropertyDescriptor Describe(MemberInfo member, string name, Type memberType, IValueConverter converter, NullabilityInfoContext nullability)
    {
        var label = member.GetCustomAttribute<LabelAttribute>()?.Text ?? DisplayNames.FromIdentifier(SourceName(member));
        var descriptor = converter.Describe(new PropertyDescriptor(name, label, PropertyKind.Object, memberType));
        if (!memberType.IsValueType)
        {
            var info = member is FieldInfo field ? nullability.Create(field) : nullability.Create((PropertyInfo)member);
            if (info.WriteState == NullabilityState.Nullable)
                descriptor = descriptor with { IsNullable = true };
        }

        if (member.GetCustomAttribute<RangeAttribute>() is { } range)
        {
            descriptor = descriptor with
            {
                Min = double.IsFinite(range.Min) ? range.Min : null,
                Max = double.IsFinite(range.Max) ? range.Max : null,
                Step = range.Step != 0 ? range.Step : descriptor.Step
            };
        }

        if (member.GetCustomAttribute<TooltipAttribute>() is { } tooltip)
            descriptor = descriptor with { Tooltip = tooltip.Text };
        if (member.GetCustomAttribute<HeaderAttribute>() is { } header)
            descriptor = descriptor with { Header = header.Text };
        if (member.GetCustomAttribute<MultilineAttribute>() is { } multiline)
            descriptor = descriptor with { Lines = multiline.Lines };
        if (member.IsDefined(typeof(HideInInspectorAttribute)))
            descriptor = descriptor with { Hidden = true };
        if (member.GetCustomAttribute<AssetFilterAttribute>() is { } filter)
            descriptor = descriptor with { AssetExtensions = filter.Extensions };
        if (member.IsDefined(typeof(AngleAttribute)))
            descriptor = descriptor with { IsAngle = true };
        if (member.GetCustomAttribute<LayerMaskAttribute>() is { } mask)
            descriptor = descriptor with { Layers = mask.Layers, IsLayerMask = true };
        else if (member.GetCustomAttribute<LayerAttribute>() is { } layer)
            descriptor = descriptor with { Layers = layer.Layers };
        if (member.GetCustomAttribute<AutoValueAttribute>() is { } auto)
            descriptor = descriptor with { AutoValue = auto.Description };
        return descriptor;
    }

    /// <summary>
    /// Public instance fields that are not read-only, other fields marked <see cref="SerializeFieldAttribute"/>, and public properties with
    /// public getters and setters, base types first, in declaration order.
    /// </summary>
    internal static IEnumerable<MemberInfo> SavedMembers(Type type)
    {
        var chain = new Stack<Type>();
        for (var t = type; t is not null && t != typeof(object) && t != typeof(ValueType); t = t.BaseType)
            chain.Push(t);

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var declaring in chain)
        {
            var members = new List<MemberInfo>();
            members.AddRange(declaring.GetFields(flags).Where(f => !f.IsInitOnly && !f.IsLiteral));
            members.AddRange(declaring.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsInitOnly && f.IsDefined(typeof(SerializeFieldAttribute))));
            members.AddRange(declaring.GetProperties(flags).Where(p =>
                p.GetIndexParameters().Length == 0 && p.GetMethod is { IsPublic: true } getter && p.SetMethod is { IsPublic: true } &&
                getter.GetBaseDefinition() == getter));
            foreach (var member in members.OrderBy(m => m.MetadataToken))
            {
                if (!member.IsDefined(typeof(TransientAttribute)) && !member.IsDefined(typeof(JsonIgnoreAttribute)))
                    yield return member;
            }
        }
    }

    internal static string JsonName(MemberInfo member) =>
        member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(SourceName(member));

    /// <summary>The member's name without leading underscores; for the backing field of an auto-property, the property's name.</summary>
    internal static string SourceName(MemberInfo member)
    {
        var name = member.Name;
        var end = name.IndexOf(">k__BackingField", StringComparison.Ordinal);
        if (name.StartsWith('<') && end > 1)
            return name[1..end];
        var trimmed = name.TrimStart('_');
        return trimmed.Length > 0 ? trimmed : name;
    }

    private static ReadMember<T> CompileRead(MemberInfo member, Type memberType, IValueConverter converter)
    {
        var target = Expression.Parameter(typeof(T).MakeByRefType(), "target");
        var node = Expression.Parameter(typeof(JsonNode), "node");
        var context = Expression.Parameter(typeof(IInstantiationContext), "context");
        var typed = Expression.Constant(converter, typeof(ValueConverter<>).MakeGenericType(memberType));
        var value = Expression.Call(typed, nameof(ValueConverter<int>.ReadOrDefault), null, node, context);
        var assign = Expression.Assign(Expression.MakeMemberAccess(target, member), value);
        return Expression.Lambda<ReadMember<T>>(assign, target, node, context).Compile();
    }

    private static WriteMember<T> CompileWrite(MemberInfo member, Type memberType, IValueConverter converter)
    {
        var source = Expression.Parameter(typeof(T).MakeByRefType(), "source");
        var context = Expression.Parameter(typeof(ICaptureContext), "context");
        var typed = Expression.Constant(converter, typeof(ValueConverter<>).MakeGenericType(memberType));
        var write = Expression.Call(typed, nameof(ValueConverter<int>.Write), null, Expression.MakeMemberAccess(source, member), context);
        return Expression.Lambda<WriteMember<T>>(write, source, context).Compile();
    }

    private static Func<T> BuildFactory()
    {
        var type = typeof(T);
        if (type.GetConstructor(Type.EmptyTypes) is { } parameterless)
            return Expression.Lambda<Func<T>>(Expression.New(parameterless)).Compile();
        if (type.IsValueType)
            return static () => default!;

        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (constructor is null)
            return static () => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        var arguments = constructor.GetParameters().Select(p => p.HasDefaultValue && p.DefaultValue is not null
            ? (Expression)Expression.Convert(Expression.Constant(p.DefaultValue), p.ParameterType)
            : Expression.Default(p.ParameterType));
        var create = Expression.Lambda<Func<T>>(Expression.New(constructor, arguments)).Compile();
        return () =>
        {
            try
            {
                return create();
            }
            catch (Exception ex) when (IsDataError(ex))
            {
                return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            }
        };
    }
}

/// <summary>Turns identifiers into names shown in the editor.</summary>
public static class DisplayNames
{
    /// <summary>Splits an identifier into words: "FollowSharpness" becomes "Follow Sharpness" and "UIScale" becomes "UI Scale".</summary>
    public static string FromIdentifier(string identifier)
    {
        var text = identifier.TrimStart('_');
        var builder = new StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (i > 0 && char.IsUpper(c) && (!char.IsUpper(text[i - 1]) || (i + 1 < text.Length && char.IsLower(text[i + 1]))))
                builder.Append(' ');
            builder.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }

        return builder.ToString();
    }
}
