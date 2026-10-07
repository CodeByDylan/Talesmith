using System.Globalization;

namespace Talesmith.Plugins;

/// <summary>Parses and compares the "major.minor.patch" versions used by plugins and the engine.</summary>
public static class PluginVersion
{
    /// <summary>Parses "major[.minor[.patch]]" into a three-part version, filling missing parts with zero.</summary>
    public static bool TryParse(string? text, out Version version) => TryParsePartial(text, out version, out _);

    /// <summary>Drops the fourth part and fills missing parts with zero, so that 1.2, 1.2.0 and 1.2.0.0 compare equal.</summary>
    public static Version Normalize(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
    }

    /// <summary>Formats a version as "major.minor.patch".</summary>
    public static string Format(Version version) => Normalize(version).ToString(3);

    /// <summary>Parses "major[.minor[.patch]]", reporting how many parts were written.</summary>
    internal static bool TryParsePartial(string? text, out Version version, out int parts)
    {
        version = new Version(0, 0, 0);
        parts = 0;
        if (string.IsNullOrEmpty(text))
            return false;

        var pieces = text.Split('.');
        if (pieces.Length > 3)
            return false;

        Span<int> numbers = [0, 0, 0];
        for (var i = 0; i < pieces.Length; i++)
        {
            if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2]);
        parts = pieces.Length;
        return true;
    }
}
