namespace Talesmith.Ecs;

/// <summary>A handle to an entity in a <see cref="World"/>.</summary>
/// <remarks>The version distinguishes an entity from later entities that reuse its id after it was destroyed.</remarks>
public readonly record struct Entity(int Id, int Version)
{
    /// <summary>A handle that never refers to a live entity.</summary>
    public static Entity Null => default;

    public bool IsNull => Version == 0;

    public override string ToString() => IsNull ? "Entity(null)" : $"Entity({Id}v{Version})";
}
