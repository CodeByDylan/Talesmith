namespace Talesmith.Diagnostics;

/// <summary>Garbage collections per generation.</summary>
public readonly record struct CollectionCounts(int Gen0, int Gen1, int Gen2)
{
    public static CollectionCounts operator +(CollectionCounts a, CollectionCounts b) => new(a.Gen0 + b.Gen0, a.Gen1 + b.Gen1, a.Gen2 + b.Gen2);
}
