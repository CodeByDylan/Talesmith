using System.Runtime.CompilerServices;

namespace Talesmith.Ecs;

/// <summary>Stores one component type for every entity of an archetype, indexed by row.</summary>
internal abstract class ComponentStorage
{
    public abstract void Resize(int capacity);

    /// <summary>Copies the value at <paramref name="row"/> into <paramref name="destination"/> at <paramref name="destinationRow"/>.</summary>
    public abstract void CopyTo(int row, ComponentStorage destination, int destinationRow);

    /// <summary>Moves the last row into <paramref name="row"/> and clears the last row.</summary>
    public abstract void RemoveSwapBack(int row, int lastRow);

    public abstract object? GetBoxed(int row);
}

internal sealed class ComponentStorage<T>(int capacity) : ComponentStorage
{
    private static readonly bool HoldsReferences = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

    public T[] Items = new T[capacity];

    public override void Resize(int capacity) => Array.Resize(ref Items, capacity);

    public override void CopyTo(int row, ComponentStorage destination, int destinationRow) =>
        Unsafe.As<ComponentStorage<T>>(destination).Items[destinationRow] = Items[row];

    public override void RemoveSwapBack(int row, int lastRow)
    {
        if (row != lastRow)
            Items[row] = Items[lastRow];
        if (HoldsReferences)
            Items[lastRow] = default!;
    }

    public override object? GetBoxed(int row) => Items[row];
}
