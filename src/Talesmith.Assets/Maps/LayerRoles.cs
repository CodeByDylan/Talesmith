namespace Talesmith.Assets.Maps;

/// <summary>Reads and writes layer roles in map files, which store them as a layer property, and infers them from layer names.</summary>
public static class LayerRoles
{
    /// <summary>The layer property that stores a role, such as <c>role = "collision"</c>.</summary>
    public const string PropertyName = "role";

    /// <summary>Parses a role name, ignoring case and a plural "s", such as "Collision" or "triggers".</summary>
    public static bool TryParse(string? text, out LayerRole role)
    {
        role = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var name = text.Trim();
        if (!char.IsAsciiLetter(name[0]))
            return false;
        if (Enum.TryParse(name, ignoreCase: true, out role) && Enum.IsDefined(role))
            return true;
        return name.Length > 1 && name.EndsWith('s') && Enum.TryParse(name[..^1], ignoreCase: true, out role) && Enum.IsDefined(role);
    }

    /// <summary>The text written for a role.</summary>
    public static string Format(LayerRole role) => role.ToString().ToLowerInvariant();

    /// <summary>Guesses the role of a layer without a role property from obvious names such as "Collision", "Triggers" or "Navigation".</summary>
    public static LayerRole Infer(string layerName, bool isObjectLayer)
    {
        ArgumentNullException.ThrowIfNull(layerName);
        LayerRole? found = null;
        foreach (var range in layerName.AsSpan().SplitAny(" _-./:()[]0123456789"))
        {
            var word = layerName.AsSpan(range);
            if (word.IsEmpty)
                continue;
            if (Matches(word, "collision", "collisions", "collider", "colliders", "solid", "solids", "walls", "blocking"))
                return LayerRole.Collision;
            if (Matches(word, "trigger", "triggers", "zones"))
                found ??= LayerRole.Trigger;
            else if (Matches(word, "navigation", "nav", "navmesh", "walkable", "walkability", "pathing"))
                found ??= LayerRole.Navigation;
            else if (!isObjectLayer && Matches(word, "decoration", "decorations", "decor", "detail", "details", "overlay"))
                found ??= LayerRole.Decoration;
        }

        return found ?? (isObjectLayer ? LayerRole.Object : LayerRole.Ground);
    }

    private static bool Matches(ReadOnlySpan<char> word, params ReadOnlySpan<string> names)
    {
        foreach (var name in names)
        {
            if (word.Equals(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
