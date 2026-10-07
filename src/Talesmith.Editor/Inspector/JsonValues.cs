using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Editor.Inspector;

/// <summary>Reads and writes the saved JSON of property values leniently, for editors: unreadable values read as null.</summary>
public static class JsonValues
{
    private static readonly ValueConverterRegistry Converters = ValueConverterRegistry.CreateDefault();

    public static double? Number(JsonNode? node)
    {
        if (node is null)
            return null;
        try
        {
            return JsonFormats.GetNumber(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    public static bool? Boolean(JsonNode? node)
    {
        if (node is null)
            return null;
        try
        {
            return JsonFormats.GetBoolean(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException)
        {
            return null;
        }
    }

    public static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : node is null ? null : node.ToJsonString();

    public static Vector2? Vector(JsonNode? node)
    {
        if (node is null)
            return null;
        try
        {
            return JsonFormats.ReadVector2(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }

    public static Color? Color(JsonNode? node) => node is null ? null : Mathematics.Color.TryParse(Text(node), out var color) ? color : null;

    public static JsonNode WriteColor(Color color) => JsonValue.Create(JsonFormats.FormatColor(color));

    /// <summary>A rectangle saved as <c>[x, y, width, height]</c>.</summary>
    public static Rect2? Rect(JsonNode? node)
    {
        if (node is not JsonArray { Count: 4 } array)
            return null;
        var values = array.Select(Number).ToArray();
        return values.Any(v => v is null) ? null : new Rect2((float)values[0]!, (float)values[1]!, (float)values[2]!, (float)values[3]!);
    }

    public static JsonArray WriteRect(Rect2 rect) => [rect.X, rect.Y, rect.Width, rect.Height];

    public static Curve? Curve(JsonNode? node) => Read<Curve>(node);

    public static JsonNode? WriteCurve(Curve curve) => Converters.Get<Curve>().Write(curve, NoReferences.Instance);

    public static Gradient? Gradient(JsonNode? node) => Read<Gradient>(node);

    public static JsonNode? WriteGradient(Gradient gradient) => Converters.Get<Gradient>().Write(gradient, NoReferences.Instance);

    /// <summary>An asset guid saved as a string, or null.</summary>
    public static AssetGuid? Asset(JsonNode? node) =>
        Text(node) is { Length: > 0 } text && AssetGuid.TryParse(text, null, out var guid) && !guid.IsEmpty ? guid : null;

    /// <summary>An entity id saved as a string, or null.</summary>
    public static Guid? EntityId(JsonNode? node) => Text(node) is { Length: > 0 } text && Guid.TryParse(text, out var id) && id != Guid.Empty ? id : null;

    /// <summary>The value a new list item or a turned-on nullable value starts with.</summary>
    public static JsonNode? DefaultFor(PropertyDescriptor property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.Kind switch
        {
            PropertyKind.Boolean => false,
            PropertyKind.Integer => property.Min is { } min && min > 0 ? (long)min : 0,
            PropertyKind.Number => property.Min is { } min && min > 0 ? min : 0,
            PropertyKind.String => "",
            PropertyKind.Enum => property.EnumNames.Count > 0 ? property.EnumNames[0] : "",
            PropertyKind.Vector2 => new JsonArray(0, 0),
            PropertyKind.Color => "#FFFFFF",
            PropertyKind.Rect => new JsonArray(0, 0, 0, 0),
            PropertyKind.Curve => WriteCurve(Mathematics.Curve.Constant(1)),
            PropertyKind.Gradient => WriteGradient(Mathematics.Gradient.Solid(Mathematics.Color.White)),
            PropertyKind.Object => DefaultObject(property),
            PropertyKind.List => new JsonArray(),
            _ => null
        };
    }

    private static JsonObject DefaultObject(PropertyDescriptor property)
    {
        var data = new JsonObject();
        foreach (var child in property.Children)
        {
            if (!child.IsNullable && DefaultFor(child) is { } value)
                data[child.Name] = value;
        }

        return data;
    }

    private static T? Read<T>(JsonNode? node)
        where T : class
    {
        if (node is null)
            return null;
        try
        {
            return Converters.Get<T>().Read(node, NoReferences.Instance);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class NoReferences : ICaptureContext, IInstantiationContext
    {
        public static NoReferences Instance { get; } = new();

        public IServiceProvider Services => EmptyServices.Instance;

        public AssetGuid GetGuid(object asset) => AssetGuid.Empty;

        public Guid GetEntityId(Entity entity) => Guid.Empty;

        public T? GetAsset<T>(AssetGuid guid)
            where T : class => null;

        public Entity GetEntity(Guid id) => Entity.Null;
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static EmptyServices Instance { get; } = new();

        public object? GetService(Type serviceType) => null;
    }
}
