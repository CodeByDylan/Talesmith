using System.Reflection;

namespace Talesmith.Systems;

/// <summary>How a system type is scheduled: its phase, order, dependencies and the execution modes it runs in.</summary>
public sealed record SystemDescriptor(Type Type, SystemPhase Phase, int Order, IReadOnlyList<Type> RunsAfter, IReadOnlyList<Type> RunsBefore)
{
    /// <summary>The modes the system runs in; see <see cref="ExecuteInAttribute"/> for the default.</summary>
    public ExecutionModes Modes { get; init; } = DefaultModes(Phase);

    public static ExecutionModes DefaultModes(SystemPhase phase) => phase == SystemPhase.PreRender ? ExecutionModes.All : ExecutionModes.Play;

    /// <summary>Reads the scheduling attributes of a system type.</summary>
    /// <exception cref="ArgumentException">The type does not implement <see cref="ISystem"/>.</exception>
    public static SystemDescriptor For(Type type)
    {
        if (!typeof(ISystem).IsAssignableFrom(type))
            throw new ArgumentException($"{type.Name} does not implement {nameof(ISystem)}.", nameof(type));

        var phase = type.GetCustomAttribute<UpdateInAttribute>()?.Phase ?? SystemPhase.Update;
        return new SystemDescriptor(
            type,
            phase,
            type.GetCustomAttribute<SystemOrderAttribute>()?.Order ?? 0,
            type.GetCustomAttributes<UpdateAfterAttribute>().Select(a => a.System).ToList(),
            type.GetCustomAttributes<UpdateBeforeAttribute>().Select(a => a.System).ToList())
        {
            Modes = ModesOf(type, phase)
        };
    }

    /// <summary>The modes from a type's <see cref="ExecuteInAttribute"/>, or the default for the phase.</summary>
    public static ExecutionModes ModesOf(Type type, SystemPhase phase) => type.GetCustomAttribute<ExecuteInAttribute>()?.Modes ?? DefaultModes(phase);
}
