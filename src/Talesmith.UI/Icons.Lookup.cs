using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Avalonia.Media;

namespace Talesmith.UI;

public static partial class Icons
{
    private static readonly Lazy<Registry> Lookup = new(Registry.Create);

    /// <summary>Gets the canonical kebab-case names of every icon, such as <c>list-tree</c> or <c>volume</c>.</summary>
    public static IReadOnlyList<string> Names => Lookup.Value.Names;

    /// <summary>Finds an icon by name, ignoring case, dashes and underscores; accepts aliases such as <c>entity</c>, <c>script</c> or <c>prefab</c>.</summary>
    public static Geometry? Find(string? name) => TryFind(name, out var icon) ? icon : null;

    /// <summary>Finds an icon by name; see <see cref="Find"/>.</summary>
    public static bool TryFind(string? name, [NotNullWhen(true)] out Geometry? icon)
    {
        icon = (Geometry?)Resolve(name)?.GetValue(null);
        return icon is not null;
    }

    /// <summary>Whether an icon has this name; see <see cref="Find"/>.</summary>
    /// <remarks>Unlike <see cref="TryFind"/>, this creates no geometry, so any thread may call it.</remarks>
    public static bool Exists(string? name) => Resolve(name) is not null;

    private static PropertyInfo? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var registry = Lookup.Value;
        var key = Normalize(name);
        if (registry.Aliases.TryGetValue(key, out var target))
            key = target;
        return registry.Properties.GetValueOrDefault(key);
    }

    private static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c is not ('-' or '_' or ' ' or '.'))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string ToKebab(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            var previous = i > 0 ? name[i - 1] : '\0';
            var boundary = i > 0 && ((char.IsUpper(c) && !char.IsDigit(previous)) || (char.IsDigit(c) && char.IsLetter(previous)));
            if (boundary)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private sealed record Registry(Dictionary<string, PropertyInfo> Properties, Dictionary<string, string> Aliases, IReadOnlyList<string> Names)
    {
        private static readonly (string Alias, string Icon)[] AliasTable =
        [
            ("entity", "box"), ("hierarchy", "list-tree"), ("component", "puzzle"), ("light", "lightbulb"),
            ("directional-light", "sun"), ("spot", "flashlight"), ("spot-light", "flashlight"), ("particles", "sparkles"),
            ("script", "file-code"), ("audio", "volume"), ("sound", "volume"), ("mute", "volume-off"),
            ("prefab", "package"), ("scene", "clapperboard"), ("rotate", "rotate-cw"), ("rectangle-select", "square-dashed"),
            ("snap", "magnet"), ("console", "terminal"), ("plugin", "plug"), ("build", "package-export"),
            ("random", "dice"), ("line", "slash"), ("auto-tile", "wand"), ("collision", "shield"), ("trigger", "flag"),
            ("object", "map-pin"), ("navigation", "compass"), ("physics", "atom"), ("rigidbody", "weight"),
            ("duplicate", "copy-plus"), ("clipboard-paste", "paste"), ("delete", "trash"), ("drag-handle", "grip-vertical"),
            ("close", "x"), ("skip-to-start", "skip-back"), ("restart", "skip-back"), ("step", "step-forward"),
            ("curve", "spline"), ("gradient", "blend"), ("keyframe", "diamond"), ("texture", "image"), ("sprite", "image"),
            ("tilemap", "map"), ("tileset", "grid"),("gear", "settings"), ("warning", "alert-triangle"),
            ("error", "alert-circle"), ("color", "palette"), ("select", "mouse-pointer"), ("pan", "hand"),
        ];

        public static Registry Create()
        {
            var properties = new Dictionary<string, PropertyInfo>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (var property in typeof(Icons).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (property.PropertyType != typeof(Geometry))
                    continue;
                properties[Normalize(property.Name)] = property;
                names.Add(ToKebab(property.Name));
            }

            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (alias, icon) in AliasTable)
                aliases[Normalize(alias)] = Normalize(icon);

            names.Sort(StringComparer.Ordinal);
            return new Registry(properties, aliases, names);
        }
    }
}
