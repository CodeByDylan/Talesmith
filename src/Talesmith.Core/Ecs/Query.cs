using System.Runtime.CompilerServices;

namespace Talesmith.Ecs;

/// <summary>A cached set of archetypes matching a <see cref="QueryDescription"/>, iterated without allocations.</summary>
/// <remarks>
/// Use <c>ForEach</c> for convenience, <c>Run</c> with a struct job for the fastest loops, or enumerate the matching archetypes and
/// work on their component spans directly. Structural changes are not allowed while iterating; use a <see cref="CommandBuffer"/>.
/// </remarks>
public sealed class Query
{
    private readonly World _world;
    private readonly List<Archetype> _archetypes = [];

    internal Query(World world, QueryDescription description)
    {
        _world = world;
        Description = description;
    }

    public QueryDescription Description { get; }

    /// <summary>Every archetype that matches, including empty ones.</summary>
    public IReadOnlyList<Archetype> Archetypes => _archetypes;

    /// <summary>The number of matching entities.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var archetype in _archetypes)
                count += archetype.Count;
            return count;
        }
    }

    public bool IsEmpty => Count == 0;

    /// <summary>Enumerates the non-empty matching archetypes.</summary>
    public ArchetypeEnumerator GetEnumerator() => new(_archetypes);

    /// <summary>Gets the only matching entity, for singletons such as the player or the main camera.</summary>
    public bool TryGetSingle(out Entity entity)
    {
        entity = Entity.Null;
        foreach (var archetype in _archetypes)
        {
            if (archetype.Count == 0)
                continue;
            if (!entity.IsNull || archetype.Count > 1)
                return false;
            entity = archetype.Entities[0];
        }

        return !entity.IsNull;
    }

    /// <summary>Calls <paramref name="action"/> for every matching entity.</summary>
    public void ForEach(Action<Entity> action)
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                var entities = archetype.Entities;
                for (var i = 0; i < entities.Length; i++)
                    action(entities[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    internal void TryAdd(Archetype archetype)
    {
        if (Description.Matches(archetype.Signature))
            _archetypes.Add(archetype);
    }

    /// <summary>Enumerates archetypes that contain at least one entity.</summary>
    public struct ArchetypeEnumerator(List<Archetype> archetypes)
    {
        private int _index = -1;

        public readonly Archetype Current => archetypes[_index];

        public bool MoveNext()
        {
            while (++_index < archetypes.Count)
            {
                if (archetypes[_index].Count > 0)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Calls <paramref name="action"/> with references to the components of every matching entity.</summary>
    public void ForEach<T1>(ForEachAction<T1> action)
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                for (var i = 0; i < entities.Length; i++)
                    action(entities[i], ref s1[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Runs a struct job over every matching entity; the job is passed by reference so it can accumulate results.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run<TJob, T1>(ref TJob job) where TJob : struct, IForEach<T1>
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                for (var i = 0; i < entities.Length; i++)
                    job.Execute(entities[i], ref s1[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Calls <paramref name="action"/> with references to the components of every matching entity.</summary>
    public void ForEach<T1, T2>(ForEachAction<T1, T2> action)
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                var s2 = archetype.GetSpan<T2>();
                for (var i = 0; i < entities.Length; i++)
                    action(entities[i], ref s1[i], ref s2[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Runs a struct job over every matching entity; the job is passed by reference so it can accumulate results.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run<TJob, T1, T2>(ref TJob job) where TJob : struct, IForEach<T1, T2>
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                var s2 = archetype.GetSpan<T2>();
                for (var i = 0; i < entities.Length; i++)
                    job.Execute(entities[i], ref s1[i], ref s2[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Calls <paramref name="action"/> with references to the components of every matching entity.</summary>
    public void ForEach<T1, T2, T3>(ForEachAction<T1, T2, T3> action)
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                var s2 = archetype.GetSpan<T2>();
                var s3 = archetype.GetSpan<T3>();
                for (var i = 0; i < entities.Length; i++)
                    action(entities[i], ref s1[i], ref s2[i], ref s3[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Runs a struct job over every matching entity; the job is passed by reference so it can accumulate results.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run<TJob, T1, T2, T3>(ref TJob job) where TJob : struct, IForEach<T1, T2, T3>
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                var s2 = archetype.GetSpan<T2>();
                var s3 = archetype.GetSpan<T3>();
                for (var i = 0; i < entities.Length; i++)
                    job.Execute(entities[i], ref s1[i], ref s2[i], ref s3[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Calls <paramref name="action"/> with references to the components of every matching entity.</summary>
    public void ForEach<T1, T2, T3, T4>(ForEachAction<T1, T2, T3, T4> action)
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                var s2 = archetype.GetSpan<T2>();
                var s3 = archetype.GetSpan<T3>();
                var s4 = archetype.GetSpan<T4>();
                for (var i = 0; i < entities.Length; i++)
                    action(entities[i], ref s1[i], ref s2[i], ref s3[i], ref s4[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }

    /// <summary>Runs a struct job over every matching entity; the job is passed by reference so it can accumulate results.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run<TJob, T1, T2, T3, T4>(ref TJob job) where TJob : struct, IForEach<T1, T2, T3, T4>
    {
        _world.BeginIteration();
        try
        {
            foreach (var archetype in _archetypes)
            {
                if (archetype.Count == 0)
                    continue;
                var entities = archetype.Entities;
                var s1 = archetype.GetSpan<T1>();
                var s2 = archetype.GetSpan<T2>();
                var s3 = archetype.GetSpan<T3>();
                var s4 = archetype.GetSpan<T4>();
                for (var i = 0; i < entities.Length; i++)
                    job.Execute(entities[i], ref s1[i], ref s2[i], ref s3[i], ref s4[i]);
            }
        }
        finally
        {
            _world.EndIteration();
        }
    }
}
