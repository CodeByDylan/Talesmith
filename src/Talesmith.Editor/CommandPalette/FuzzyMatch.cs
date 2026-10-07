namespace Talesmith.Editor.CommandPalette;

/// <summary>Scores how well a query matches a name, letting the letters of the query appear anywhere in order, as editors' quick pickers do.</summary>
public static class FuzzyMatch
{
    /// <summary>Returns 0 when the query's letters do not appear in order in <paramref name="text"/>; otherwise a score that is higher for
    /// matches at the start, at word starts and in runs of consecutive letters, and for shorter names. Spaces in the query are ignored.</summary>
    public static double Score(string text, string query)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        var pattern = query.Replace(" ", "", StringComparison.Ordinal);
        if (pattern.Length == 0)
            return 1;
        if (pattern.Length > text.Length)
            return 0;

        double score = 0;
        var t = 0;
        var previousMatch = -2;
        foreach (var q in pattern)
        {
            var found = -1;
            var best = double.MinValue;
            for (var i = t; i < text.Length; i++)
            {
                if (char.ToUpperInvariant(text[i]) != char.ToUpperInvariant(q))
                    continue;
                var bonus = Bonus(text, i, previousMatch);
                if (bonus > best)
                {
                    best = bonus;
                    found = i;
                    if (bonus >= 8)
                        break;
                }

                if (found >= 0 && i > found + 8)
                    break;
            }

            if (found < 0)
                return 0;
            score += best - Math.Min(found - t, 6) * 0.25;
            previousMatch = found;
            t = found + 1;
        }

        if (text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
            score += 12;
        else if (text.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            score += 6;
        return Math.Max(0.01, score - text.Length * 0.05);
    }

    private static double Bonus(string text, int index, int previousMatch)
    {
        if (index == 0)
            return 10;
        if (index == previousMatch + 1)
            return 6;
        var before = text[index - 1];
        if (before is ' ' or '.' or '/' or '\\' or '-' or '_' or '(' or ':')
            return 8;
        if (char.IsLower(before) && char.IsUpper(text[index]))
            return 7;
        return 1;
    }
}
