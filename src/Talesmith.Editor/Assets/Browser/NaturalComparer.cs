namespace Talesmith.Editor.Assets.Browser;

/// <summary>Compares names the way people read them, ignoring case and ordering numbers by value: "walk 2" before "walk 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
            return 0;
        if (x is null)
            return -1;
        if (y is null)
            return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                    i++;
                while (j < y.Length && char.IsAsciiDigit(y[j]))
                    j++;
                var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                var numberY = y.AsSpan(startY, j - startY).TrimStart('0');
                if (numberX.Length != numberY.Length)
                    return numberX.Length.CompareTo(numberY.Length);
                var digits = numberX.SequenceCompareTo(numberY);
                if (digits != 0)
                    return digits;
                continue;
            }

            var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
            if (c != 0)
                return c;
            i++;
            j++;
        }

        var length = (x.Length - i).CompareTo(y.Length - j);
        return length != 0 ? length : string.CompareOrdinal(x, y);
    }
}
