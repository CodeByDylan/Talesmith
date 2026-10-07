using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Talesmith.Plugins;

/// <summary>A set of acceptable versions, written like npm ranges: "1.2.3", "^1.2", "~1.2", "&gt;=1.2 &lt;2.0", "1.x || 2.x" or "*".</summary>
/// <remarks>
/// Space-separated comparators must all match; "||" separates alternatives. A version with missing parts means every version with that
/// prefix: "1.2" is "&gt;=1.2.0 &lt;1.3.0" and "&lt;=1.2" is "&lt;1.3.0". "^" allows changes that keep the first non-zero part, "~"
/// allows patch changes. Versions compare by major, minor and patch.
/// </remarks>
public sealed partial class VersionRange : IEquatable<VersionRange>
{
    private enum Operator
    {
        Equal,
        Greater,
        GreaterOrEqual,
        Less,
        LessOrEqual
    }

    private readonly record struct Comparator(Operator Operator, Version Version)
    {
        public bool Matches(Version version)
        {
            var order = version.CompareTo(Version);
            return Operator switch
            {
                Operator.Equal => order == 0,
                Operator.Greater => order > 0,
                Operator.GreaterOrEqual => order >= 0,
                Operator.Less => order < 0,
                _ => order <= 0
            };
        }
    }

    private readonly Comparator[][] _alternatives;
    private readonly string _text;

    private VersionRange(Comparator[][] alternatives, string text)
    {
        _alternatives = alternatives;
        _text = text;
    }

    /// <summary>Every version.</summary>
    public static VersionRange Any { get; } = new([[]], "*");

    /// <summary>The given version or anything newer.</summary>
    public static VersionRange AtLeast(Version minimum)
    {
        var version = PluginVersion.Normalize(minimum);
        return new VersionRange([[new Comparator(Operator.GreaterOrEqual, version)]], $">={PluginVersion.Format(version)}");
    }

    /// <summary>Whether this is <see cref="Any"/> or another range every version satisfies.</summary>
    public bool IsAny => _alternatives.Any(a => a.Length == 0);

    public static VersionRange Parse(string text) =>
        TryParse(text, out var range, out var error) ? range : throw new FormatException(error);

    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range) => TryParse(text, out range, out _);

    /// <summary>Parses a range, describing what is wrong when it cannot.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out VersionRange? range, [NotNullWhen(false)] out string? error)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "a version range must not be empty; use \"*\" for any version.";
            return false;
        }

        var normalized = Whitespace().Replace(OperatorSpacing().Replace(text.Trim(), "$1"), " ");
        var alternatives = new List<Comparator[]>();
        foreach (var alternative in normalized.Split("||"))
        {
            var comparators = new List<Comparator>();
            var tokens = alternative.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                error = $"\"{text}\" has an empty alternative around \"||\".";
                return false;
            }

            foreach (var token in tokens)
            {
                if (!TryParseComparator(token, comparators))
                {
                    error = $"\"{token}\" in \"{text}\" is not a version or comparison; use forms such as \"1.2.0\", \"^1.2\", \"~1.2\", \">=1.2 <2.0\" or \"*\".";
                    return false;
                }
            }

            alternatives.Add([.. comparators]);
        }

        error = null;
        range = new VersionRange([.. alternatives], normalized);
        return true;
    }

    public bool IsSatisfiedBy(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var normalized = PluginVersion.Normalize(version);
        return _alternatives.Any(alternative => alternative.All(c => c.Matches(normalized)));
    }

    public override string ToString() => _text;

    public bool Equals(VersionRange? other) => other is not null && other._text == _text;

    public override bool Equals(object? obj) => Equals(obj as VersionRange);

    public override int GetHashCode() => _text.GetHashCode(StringComparison.Ordinal);

    private static bool TryParseComparator(string token, List<Comparator> comparators)
    {
        if (token is "*" or "x" or "X")
            return true;

        var (op, rest) = token switch
        {
            _ when token.StartsWith(">=", StringComparison.Ordinal) => (">=", token[2..]),
            _ when token.StartsWith("<=", StringComparison.Ordinal) => ("<=", token[2..]),
            _ when token.StartsWith('>') => (">", token[1..]),
            _ when token.StartsWith('<') => ("<", token[1..]),
            _ when token.StartsWith('=') => ("=", token[1..]),
            _ when token.StartsWith('^') => ("^", token[1..]),
            _ when token.StartsWith('~') => ("~", token[1..]),
            _ => ("", token)
        };

        rest = WildcardSuffix().Replace(rest, "");
        if (!PluginVersion.TryParsePartial(rest, out var version, out var parts))
            return false;

        var next = Increment(version, parts);
        switch (op)
        {
            case ">=":
                comparators.Add(new Comparator(Operator.GreaterOrEqual, version));
                break;
            case "<":
                comparators.Add(new Comparator(Operator.Less, version));
                break;
            case ">":
                comparators.Add(parts == 3 ? new Comparator(Operator.Greater, version) : new Comparator(Operator.GreaterOrEqual, next));
                break;
            case "<=":
                comparators.Add(parts == 3 ? new Comparator(Operator.LessOrEqual, version) : new Comparator(Operator.Less, next));
                break;
            case "^":
                comparators.Add(new Comparator(Operator.GreaterOrEqual, version));
                comparators.Add(new Comparator(Operator.Less, CaretLimit(version, parts)));
                break;
            case "~":
                comparators.Add(new Comparator(Operator.GreaterOrEqual, version));
                comparators.Add(new Comparator(Operator.Less, parts == 1 ? next : new Version(version.Major, version.Minor + 1, 0)));
                break;
            default:
                if (parts == 3)
                {
                    comparators.Add(new Comparator(Operator.Equal, version));
                }
                else
                {
                    comparators.Add(new Comparator(Operator.GreaterOrEqual, version));
                    comparators.Add(new Comparator(Operator.Less, next));
                }

                break;
        }

        return true;
    }

    private static Version Increment(Version version, int parts) => parts switch
    {
        1 => new Version(version.Major + 1, 0, 0),
        2 => new Version(version.Major, version.Minor + 1, 0),
        _ => new Version(version.Major, version.Minor, version.Build + 1)
    };

    private static Version CaretLimit(Version version, int parts)
    {
        if (version.Major > 0 || parts == 1)
            return new Version(version.Major + 1, 0, 0);
        if (version.Minor > 0 || parts == 2)
            return new Version(0, version.Minor + 1, 0);
        return new Version(0, 0, version.Build + 1);
    }

    [GeneratedRegex(@"(>=|<=|>|<|=|\^|~)\s+", RegexOptions.CultureInvariant)]
    private static partial Regex OperatorSpacing();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(\.[xX*])+$", RegexOptions.CultureInvariant)]
    private static partial Regex WildcardSuffix();
}
