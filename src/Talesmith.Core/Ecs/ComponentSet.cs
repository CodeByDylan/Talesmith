using System.Numerics;
using System.Runtime.CompilerServices;

namespace Talesmith.Ecs;

/// <summary>An immutable set of component types, stored as a bit set.</summary>
public readonly struct ComponentSet : IEquatable<ComponentSet>
{
    private readonly ulong[]? _bits;

    private ComponentSet(ulong[]? bits) => _bits = bits;

    public static ComponentSet Empty => default;

    public bool IsEmpty => _bits is null;

    /// <summary>The ids of the contained component types in ascending order.</summary>
    public IEnumerable<int> Ids
    {
        get
        {
            if (_bits is null)
                yield break;
            for (var word = 0; word < _bits.Length; word++)
            {
                var value = _bits[word];
                while (value != 0)
                {
                    var bit = BitOperations.TrailingZeroCount(value);
                    yield return word * 64 + bit;
                    value &= value - 1;
                }
            }
        }
    }

    public int Count
    {
        get
        {
            if (_bits is null)
                return 0;
            var count = 0;
            foreach (var word in _bits)
                count += BitOperations.PopCount(word);
            return count;
        }
    }

    public static ComponentSet Of(IEnumerable<int> ids)
    {
        ulong[]? bits = null;
        foreach (var id in ids)
            Set(ref bits, id);
        return new ComponentSet(bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(int id)
    {
        var word = id >> 6;
        return _bits is not null && word < _bits.Length && (_bits[word] & (1UL << (id & 63))) != 0;
    }

    /// <summary>Whether every type in <paramref name="other"/> is also in this set.</summary>
    public bool ContainsAll(ComponentSet other)
    {
        if (other._bits is null)
            return true;
        for (var i = 0; i < other._bits.Length; i++)
        {
            var mine = _bits is not null && i < _bits.Length ? _bits[i] : 0;
            if ((mine & other._bits[i]) != other._bits[i])
                return false;
        }

        return true;
    }

    /// <summary>Whether this set shares at least one type with <paramref name="other"/>.</summary>
    public bool Overlaps(ComponentSet other)
    {
        if (_bits is null || other._bits is null)
            return false;
        var length = Math.Min(_bits.Length, other._bits.Length);
        for (var i = 0; i < length; i++)
        {
            if ((_bits[i] & other._bits[i]) != 0)
                return true;
        }

        return false;
    }

    public ComponentSet With(int id)
    {
        if (Contains(id))
            return this;
        var bits = _bits is null ? null : (ulong[])_bits.Clone();
        Set(ref bits, id);
        return new ComponentSet(bits);
    }

    public ComponentSet Without(int id)
    {
        if (!Contains(id))
            return this;
        var bits = (ulong[])_bits!.Clone();
        bits[id >> 6] &= ~(1UL << (id & 63));
        var last = bits.Length - 1;
        while (last >= 0 && bits[last] == 0)
            last--;
        return last < 0 ? Empty : new ComponentSet(last == bits.Length - 1 ? bits : bits[..(last + 1)]);
    }

    public bool Equals(ComponentSet other)
    {
        if (_bits is null || other._bits is null)
            return _bits is null && other._bits is null;
        return _bits.AsSpan().SequenceEqual(other._bits);
    }

    public override bool Equals(object? obj) => obj is ComponentSet other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (_bits is not null)
        {
            foreach (var word in _bits)
                hash.Add(word);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(ComponentSet left, ComponentSet right) => left.Equals(right);

    public static bool operator !=(ComponentSet left, ComponentSet right) => !left.Equals(right);

    public override string ToString() => $"[{string.Join(", ", Ids.Select(id => ComponentType.FromId(id).Type.Name))}]";

    private static void Set(ref ulong[]? bits, int id)
    {
        var word = id >> 6;
        if (bits is null)
            bits = new ulong[word + 1];
        else if (word >= bits.Length)
            Array.Resize(ref bits, word + 1);
        bits[word] |= 1UL << (id & 63);
    }
}
