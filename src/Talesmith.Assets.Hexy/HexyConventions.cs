using System.Globalization;
using System.Numerics;
using System.Text;
using Talesmith.Assets.Maps;

namespace Talesmith.Assets.Hexy;

/// <summary>How Talesmith stores data Hexy has no field for: in custom properties, which Hexy shows, keeps and writes back unchanged.</summary>
/// <remarks>
/// A layer's <see cref="LayerRole"/> is the string property <c>role</c>, written only when it differs from what the layer's name
/// suggests. A tile's <see cref="TileInfo.Collision"/> polygons are the string property <c>collision</c>: polygons separated by
/// <c>;</c>, points by spaces and coordinates by a comma, in pixels from the cell center, such as <c>-32,-16 32,-16 0,16</c>.
/// Properties with these names that do not parse stay ordinary properties.
/// </remarks>
internal static class HexyConventions
{
    public const string RoleProperty = LayerRoles.PropertyName;
    public const string CollisionProperty = "collision";

    public static bool TryParseCollision(PropertyValue value, out IReadOnlyList<IReadOnlyList<Vector2>> polygons)
    {
        polygons = [];
        if (value.Type != PropertyType.String || string.IsNullOrWhiteSpace(value.Raw))
            return false;

        var result = new List<IReadOnlyList<Vector2>>();
        foreach (var polygonText in value.Raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var points = new List<Vector2>();
            foreach (var pointText in polygonText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var comma = pointText.IndexOf(',', StringComparison.Ordinal);
                if (comma <= 0
                    || !float.TryParse(pointText.AsSpan(0, comma), NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !float.TryParse(pointText.AsSpan(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || !float.IsFinite(x) || !float.IsFinite(y))
                    return false;
                points.Add(new Vector2(x, y));
            }

            if (points.Count < 3)
                return false;
            result.Add(points.ToArray());
        }

        if (result.Count == 0)
            return false;
        polygons = result.ToArray();
        return true;
    }

    public static PropertyValue FormatCollision(IReadOnlyList<IReadOnlyList<Vector2>> polygons)
    {
        var text = new StringBuilder();
        foreach (var polygon in polygons)
        {
            if (text.Length > 0)
                text.Append("; ");
            for (var i = 0; i < polygon.Count; i++)
            {
                if (i > 0)
                    text.Append(' ');
                text.Append(polygon[i].X.ToString(CultureInfo.InvariantCulture)).Append(',').Append(polygon[i].Y.ToString(CultureInfo.InvariantCulture));
            }
        }

        return PropertyValue.FromString(text.ToString());
    }

    public static bool SameCollision(IReadOnlyList<IReadOnlyList<Vector2>> a, IReadOnlyList<IReadOnlyList<Vector2>> b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Count != b[i].Count)
                return false;
            for (var j = 0; j < a[i].Count; j++)
            {
                if (a[i][j] != b[i][j])
                    return false;
            }
        }

        return true;
    }
}
