namespace Talesmith.Systems;

/// <summary>Runs the system in the given phase. Systems without it run in <see cref="SystemPhase.Update"/>.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class UpdateInAttribute(SystemPhase phase) : Attribute
{
    public SystemPhase Phase { get; } = phase;
}

/// <summary>Runs the system after another system of the same phase.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UpdateAfterAttribute(Type system) : Attribute
{
    public Type System { get; } = system;
}

/// <summary>Runs the system before another system of the same phase.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UpdateBeforeAttribute(Type system) : Attribute
{
    public Type System { get; } = system;
}

/// <summary>Orders systems of a phase that have no dependency between them; lower values run first. The default is 0.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SystemOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}
