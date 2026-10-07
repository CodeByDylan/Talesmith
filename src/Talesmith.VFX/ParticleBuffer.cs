using System.Buffers;
using System.Numerics;

namespace Talesmith.VFX;

/// <summary>The alive particles of one emitter, stored as one array per attribute so loops stream through memory and vectorize.</summary>
/// <remarks>
/// Particles <c>0</c> to <see cref="Count"/> - 1 are alive; removing one moves the last particle into its slot, so indices are not stable
/// across steps. Arrays come from <see cref="ArrayPool{T}"/> and grow by doubling; <see cref="Dispose"/> returns them.
/// </remarks>
public sealed class ParticleBuffer : IDisposable
{
    private const int InitialCapacity = 64;

    internal float[] PositionXArray = [];
    internal float[] PositionYArray = [];
    internal float[] VelocityXArray = [];
    internal float[] VelocityYArray = [];
    internal float[] AgeArray = [];
    internal float[] AgeRateArray = [];
    internal float[] SizeArray = [];
    internal float[] RotationArray = [];
    internal float[] AngularVelocityArray = [];
    internal float[] SeedArray = [];
    internal Vector4[] ColorArray = [];

    public int Count { get; private set; }

    public int Capacity { get; private set; }

    public Span<float> PositionX => PositionXArray.AsSpan(0, Count);

    public Span<float> PositionY => PositionYArray.AsSpan(0, Count);

    public Span<float> VelocityX => VelocityXArray.AsSpan(0, Count);

    public Span<float> VelocityY => VelocityYArray.AsSpan(0, Count);

    /// <summary>How far through its life each particle is, from 0 at birth to 1 at death.</summary>
    public Span<float> Age => AgeArray.AsSpan(0, Count);

    /// <summary>The age gained per second: one over the lifetime in seconds.</summary>
    public Span<float> AgeRate => AgeRateArray.AsSpan(0, Count);

    /// <summary>The starting size in world units, before size over lifetime.</summary>
    public Span<float> Size => SizeArray.AsSpan(0, Count);

    /// <summary>Rotation in radians, clockwise.</summary>
    public Span<float> Rotation => RotationArray.AsSpan(0, Count);

    public Span<float> AngularVelocity => AngularVelocityArray.AsSpan(0, Count);

    /// <summary>A random number from 0 to 1 fixed for each particle's life, for per-particle variation.</summary>
    public Span<float> Seed => SeedArray.AsSpan(0, Count);

    /// <summary>The starting straight-alpha color with components from 0 to 1, before color over lifetime.</summary>
    public Span<Vector4> Color => ColorArray.AsSpan(0, Count);

    /// <summary>Marks a particle for removal at the end of the step.</summary>
    public void Kill(int index) => AgeArray[index] = 1;

    /// <summary>Adds <paramref name="count"/> uninitialized particles and returns the index of the first.</summary>
    internal int Add(int count)
    {
        EnsureCapacity(Count + count);
        var first = Count;
        Count += count;
        return first;
    }

    /// <summary>Removes particles whose age reached 1 and returns how many were removed.</summary>
    internal int RemoveDead()
    {
        var age = AgeArray;
        var count = Count;
        var i = 0;
        while (i < count)
        {
            if (age[i] < 1)
            {
                i++;
                continue;
            }

            count--;
            if (i != count)
                Move(count, i);
        }

        var removed = Count - count;
        Count = count;
        return removed;
    }

    internal void Clear() => Count = 0;

    public void Dispose()
    {
        Return(ref PositionXArray);
        Return(ref PositionYArray);
        Return(ref VelocityXArray);
        Return(ref VelocityYArray);
        Return(ref AgeArray);
        Return(ref AgeRateArray);
        Return(ref SizeArray);
        Return(ref RotationArray);
        Return(ref AngularVelocityArray);
        Return(ref SeedArray);
        Return(ref ColorArray);
        Count = 0;
        Capacity = 0;
    }

    private void Move(int from, int to)
    {
        PositionXArray[to] = PositionXArray[from];
        PositionYArray[to] = PositionYArray[from];
        VelocityXArray[to] = VelocityXArray[from];
        VelocityYArray[to] = VelocityYArray[from];
        AgeArray[to] = AgeArray[from];
        AgeRateArray[to] = AgeRateArray[from];
        SizeArray[to] = SizeArray[from];
        RotationArray[to] = RotationArray[from];
        AngularVelocityArray[to] = AngularVelocityArray[from];
        SeedArray[to] = SeedArray[from];
        ColorArray[to] = ColorArray[from];
    }

    private void EnsureCapacity(int required)
    {
        if (required <= Capacity)
            return;
        var capacity = Math.Max(Math.Max(InitialCapacity, Capacity * 2), required);
        Grow(ref PositionXArray, capacity);
        Grow(ref PositionYArray, capacity);
        Grow(ref VelocityXArray, capacity);
        Grow(ref VelocityYArray, capacity);
        Grow(ref AgeArray, capacity);
        Grow(ref AgeRateArray, capacity);
        Grow(ref SizeArray, capacity);
        Grow(ref RotationArray, capacity);
        Grow(ref AngularVelocityArray, capacity);
        Grow(ref SeedArray, capacity);
        Grow(ref ColorArray, capacity);
        Capacity = capacity;
    }

    private void Grow<T>(ref T[] array, int capacity)
    {
        var grown = ArrayPool<T>.Shared.Rent(capacity);
        array.AsSpan(0, Count).CopyTo(grown);
        Return(ref array);
        array = grown;
    }

    private static void Return<T>(ref T[] array)
    {
        if (array.Length > 0)
            ArrayPool<T>.Shared.Return(array);
        array = [];
    }
}
