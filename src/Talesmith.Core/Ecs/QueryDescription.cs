namespace Talesmith.Ecs;

/// <summary>Describes which entities a <see cref="Query"/> matches: all of some component types, any of others and none of a third set.</summary>
public readonly record struct QueryDescription
{
    /// <summary>Component types an entity must have.</summary>
    public ComponentSet All { get; init; }

    /// <summary>Component types of which an entity must have at least one; ignored when empty.</summary>
    public ComponentSet Any { get; init; }

    /// <summary>Component types an entity must not have.</summary>
    public ComponentSet None { get; init; }

    public static QueryDescription With<T>() => new() { All = ComponentSet.Empty.With(ComponentTypeCache<T>.Id) };

    public QueryDescription And<T>() => this with { All = All.With(ComponentTypeCache<T>.Id) };

    public QueryDescription WithAny<T>() => this with { Any = Any.With(ComponentTypeCache<T>.Id) };

    public QueryDescription Without<T>() => this with { None = None.With(ComponentTypeCache<T>.Id) };

    public bool Matches(ComponentSet signature) =>
        signature.ContainsAll(All) && (Any.IsEmpty || signature.Overlaps(Any)) && !signature.Overlaps(None);
}
