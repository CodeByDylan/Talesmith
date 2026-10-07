namespace Talesmith.Ecs;

/// <summary>Processes one entity of a query with 1 component.</summary>
public delegate void ForEachAction<T1>(Entity entity, ref T1 c1);

/// <summary>A struct job run by <see cref="Query.Run{TJob, T1}"/>; being a struct lets the JIT inline <see cref="Execute"/>.</summary>
public interface IForEach<T1>
{
    void Execute(Entity entity, ref T1 c1);
}

/// <summary>Processes one entity of a query with 2 components.</summary>
public delegate void ForEachAction<T1, T2>(Entity entity, ref T1 c1, ref T2 c2);

/// <summary>A struct job run by <see cref="Query.Run{TJob, T1, T2}"/>; being a struct lets the JIT inline <see cref="Execute"/>.</summary>
public interface IForEach<T1, T2>
{
    void Execute(Entity entity, ref T1 c1, ref T2 c2);
}

/// <summary>Processes one entity of a query with 3 components.</summary>
public delegate void ForEachAction<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);

/// <summary>A struct job run by <see cref="Query.Run{TJob, T1, T2, T3}"/>; being a struct lets the JIT inline <see cref="Execute"/>.</summary>
public interface IForEach<T1, T2, T3>
{
    void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);
}

/// <summary>Processes one entity of a query with 4 components.</summary>
public delegate void ForEachAction<T1, T2, T3, T4>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);

/// <summary>A struct job run by <see cref="Query.Run{TJob, T1, T2, T3, T4}"/>; being a struct lets the JIT inline <see cref="Execute"/>.</summary>
public interface IForEach<T1, T2, T3, T4>
{
    void Execute(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);
}

