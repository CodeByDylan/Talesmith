namespace Talesmith.Grids;

/// <summary>Returns the cost of entering a cell, or <see cref="float.PositiveInfinity"/> when the cell cannot be entered.</summary>
public delegate float CellCost(GridCoord cell);

/// <summary>Finds shortest paths on any <see cref="IGridLayout"/> with A*.</summary>
/// <remarks>Buffers are reused between searches, so one instance per thread keeps pathfinding allocation-free after warm-up.</remarks>
public sealed class GridPathfinder(IGridLayout layout)
{
    private readonly PriorityQueue<GridCoord, float> _open = new();
    private readonly Dictionary<GridCoord, GridCoord> _cameFrom = new();
    private readonly Dictionary<GridCoord, float> _costSoFar = new();

    public IGridLayout Layout { get; } = layout;

    /// <summary>Finds a path from <paramref name="start"/> to <paramref name="goal"/>, including both, into <paramref name="path"/>.</summary>
    /// <param name="maxVisited">Gives up after visiting this many cells, which bounds the cost of unreachable goals.</param>
    /// <returns>False when the goal cannot be reached within the limit; <paramref name="path"/> is then empty.</returns>
    public bool TryFindPath(GridCoord start, GridCoord goal, CellCost cost, List<GridCoord> path, int maxVisited = 10_000)
    {
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        _open.Clear();
        _cameFrom.Clear();
        _costSoFar.Clear();

        if (float.IsPositiveInfinity(cost(goal)))
            return false;

        _open.Enqueue(start, 0);
        _costSoFar[start] = 0;
        var visited = 0;
        while (_open.TryDequeue(out var current, out _))
        {
            if (current == goal)
            {
                for (var cell = goal; cell != start; cell = _cameFrom[cell])
                    path.Add(cell);
                path.Add(start);
                path.Reverse();
                return true;
            }

            if (++visited > maxVisited)
                break;

            var currentCost = _costSoFar[current];
            foreach (var offset in Layout.NeighborOffsets)
            {
                var next = current + offset;
                var step = cost(next);
                if (float.IsPositiveInfinity(step) || step < 0)
                    continue;

                var newCost = currentCost + step;
                if (_costSoFar.TryGetValue(next, out var known) && known <= newCost)
                    continue;

                _costSoFar[next] = newCost;
                _cameFrom[next] = current;
                _open.Enqueue(next, newCost + Layout.Distance(next, goal));
            }
        }

        return false;
    }
}
