namespace Talesmith.Assets.Maps.Editing;

/// <summary>A reversible change to a map, compact enough for an undo stack to keep many of them.</summary>
/// <remarks>
/// Applying an edit returns the edit that reverts it, so an undo stack keeps one edit per step and swaps it for the returned one on
/// every undo and redo. Edits refer to layers and tilesets by instance, which stay valid across removals because reverting a removal
/// puts the same instance back.
/// </remarks>
public interface IMapEdit
{
    /// <summary>The approximate memory the edit keeps alive, in bytes, for bounding undo history.</summary>
    long EstimatedSize { get; }

    /// <summary>Applies the change and returns the edit that reverts it.</summary>
    /// <exception cref="InvalidOperationException">The map no longer contains what the edit changes.</exception>
    IMapEdit Apply(TileMap map);
}

/// <summary>Several edits applied in order as one step; reverting applies their inverses in reverse order.</summary>
public sealed class CompositeEdit(IReadOnlyList<IMapEdit> edits) : IMapEdit
{
    public IReadOnlyList<IMapEdit> Edits { get; } = edits ?? throw new ArgumentNullException(nameof(edits));

    public long EstimatedSize
    {
        get
        {
            long size = 32;
            foreach (var edit in Edits)
                size += edit.EstimatedSize;
            return size;
        }
    }

    public IMapEdit Apply(TileMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        var inverses = new IMapEdit[Edits.Count];
        for (var i = 0; i < Edits.Count; i++)
            inverses[Edits.Count - 1 - i] = Edits[i].Apply(map);
        return new CompositeEdit(inverses);
    }
}
