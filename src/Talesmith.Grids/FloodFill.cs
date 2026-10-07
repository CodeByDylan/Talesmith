namespace Talesmith.Grids;

/// <summary>Decides whether a cell belongs to a flood-filled region; a struct implementation lets the search inline it.</summary>
public interface ICellPredicate
{
    bool Matches(GridCoord cell);
}

/// <summary>The outcome of a flood fill.</summary>
/// <param name="Count">The number of cells collected.</param>
/// <param name="ReachedLimit">Whether the search stopped at the cell limit before the region was exhausted.</param>
public readonly record struct FloodFillResult(int Count, bool ReachedLimit);

/// <summary>Collects connected regions breadth-first over edge neighbors, bounded by a cell limit because empty space is unbounded.</summary>
public static class FloodFill
{
    /// <summary>Adds the region connected to <paramref name="start"/> whose cells match to <paramref name="output"/>.</summary>
    /// <returns>An empty result when <paramref name="start"/> does not match.</returns>
    public static FloodFillResult Collect<TPredicate>(GridTopology topology, GridCoord start, ref TPredicate predicate, int maxCells, CellSet output,
        CancellationToken cancellationToken = default)
        where TPredicate : struct, ICellPredicate
    {
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCells);
        if (!predicate.Matches(start))
            return default;

        var visited = new CellSet { start };
        var frontier = new Queue<GridCoord>();
        frontier.Enqueue(start);
        var directions = topology.Directions;
        var count = 0;
        while (frontier.TryDequeue(out var cell))
        {
            if ((count & 0xFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            output.Add(cell);
            if (++count >= maxCells)
                return new FloodFillResult(count, frontier.Count > 0 || HasUnvisitedMatch(directions, cell, ref predicate, visited));

            foreach (var direction in directions)
            {
                var neighbor = cell + direction;
                if (visited.Add(neighbor) && predicate.Matches(neighbor))
                    frontier.Enqueue(neighbor);
            }
        }

        return new FloodFillResult(count, false);
    }

    /// <inheritdoc cref="Collect{TPredicate}"/>
    public static FloodFillResult Collect(GridTopology topology, GridCoord start, Func<GridCoord, bool> matches, int maxCells, CellSet output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var predicate = new DelegatePredicate(matches);
        return Collect(topology, start, ref predicate, maxCells, output, cancellationToken);
    }

    private static bool HasUnvisitedMatch<TPredicate>(ReadOnlySpan<GridCoord> directions, GridCoord cell, ref TPredicate predicate, CellSet visited)
        where TPredicate : struct, ICellPredicate
    {
        foreach (var direction in directions)
        {
            var neighbor = cell + direction;
            if (!visited.Contains(neighbor) && predicate.Matches(neighbor))
                return true;
        }

        return false;
    }

    private readonly struct DelegatePredicate(Func<GridCoord, bool> matches) : ICellPredicate
    {
        public bool Matches(GridCoord cell) => matches(cell);
    }
}
