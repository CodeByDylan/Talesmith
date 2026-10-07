using Talesmith.Diagnostics;
using Talesmith.Ecs;
using Talesmith.Time;

namespace Talesmith.Systems;

/// <summary>A system instance with its schedule, as run by a <see cref="SystemScheduler"/>.</summary>
public sealed class ScheduledSystem(SystemDescriptor descriptor, ISystem instance)
{
    public SystemDescriptor Descriptor { get; } = descriptor;

    public ISystem Instance { get; } = instance;

    /// <summary>Disabled systems are skipped until enabled again.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Updates that threw in a row; reset by a successful update.</summary>
    public int ConsecutiveFailures { get; internal set; }

    /// <summary>The profiler marker that times this system, named "Systems/{type}".</summary>
    public ProfilerMarker Marker { get; } = ProfilerMarker.Get($"Systems/{descriptor.Type.Name}", "Systems");
}

/// <summary>Runs a world's systems phase by phase in dependency order, timing each one and applying its deferred changes.</summary>
/// <remarks>
/// Within a phase, a system runs after every system it declares with <see cref="UpdateAfterAttribute"/> and before every system it
/// declares with <see cref="UpdateBeforeAttribute"/>; otherwise lower <see cref="SystemOrderAttribute"/> values run first, then
/// registration order. Dependencies on systems that are not registered or live in other phases are ignored.
/// </remarks>
public sealed class SystemScheduler
{
    /// <summary>A system that throws this many updates in a row is disabled when an error handler is set.</summary>
    public const int MaxConsecutiveFailures = 3;

    private static readonly SystemPhase[] Phases = Enum.GetValues<SystemPhase>();

    private readonly World _world;
    private readonly Profiler _profiler;
    private readonly CommandBuffer _commands;
    private readonly ScheduledSystem[][] _byPhase;
    private readonly Action<ScheduledSystem, Exception>? _onError;
    private bool _started;

    /// <param name="onError">Receives exceptions thrown by systems. Without it, exceptions propagate to the caller.</param>
    /// <exception cref="InvalidOperationException">The dependencies of a phase form a cycle.</exception>
    public SystemScheduler(World world, IEnumerable<ScheduledSystem> systems, Profiler profiler, Action<ScheduledSystem, Exception>? onError = null)
    {
        _onError = onError;
        _world = world;
        _profiler = profiler;
        _commands = new CommandBuffer(world);
        var all = systems.ToList();
        Systems = all;
        _byPhase = new ScheduledSystem[Phases.Length][];
        foreach (var phase in Phases)
            _byPhase[(int)phase] = Order(all.Where(s => s.Descriptor.Phase == phase).ToList(), phase);
    }

    /// <summary>The mode the world is used in; systems whose <see cref="SystemDescriptor.Modes"/> exclude it are skipped.</summary>
    public ExecutionModes Mode { get; set; } = ExecutionModes.Play;

    /// <summary>Every system in registration order.</summary>
    public IReadOnlyList<ScheduledSystem> Systems { get; }

    /// <summary>Calls <see cref="ISystemLifecycle.OnStart"/> on systems that implement it, in run order.</summary>
    public void Start()
    {
        if (_started)
            return;
        _started = true;
        foreach (var phase in Phases)
        {
            foreach (var system in _byPhase[(int)phase])
                (system.Instance as ISystemLifecycle)?.OnStart(_world);
        }

        _commands.Playback();
    }

    /// <summary>Calls <see cref="ISystemLifecycle.OnStop"/> in reverse run order.</summary>
    public void Stop()
    {
        if (!_started)
            return;
        _started = false;
        for (var p = Phases.Length - 1; p >= 0; p--)
        {
            var systems = _byPhase[p];
            for (var i = systems.Length - 1; i >= 0; i--)
                (systems[i].Instance as ISystemLifecycle)?.OnStop(_world);
        }
    }

    /// <summary>Runs every enabled system of a phase once.</summary>
    public void Run(SystemPhase phase, in GameTime time)
    {
        var context = new SystemContext(_world, time, _commands);
        foreach (var system in _byPhase[(int)phase])
        {
            if (!system.Enabled || (system.Descriptor.Modes & Mode) == 0)
                continue;

            using (_profiler.Measure(system.Marker))
            {
                if (_onError is null)
                {
                    system.Instance.Update(context);
                }
                else
                {
                    try
                    {
                        system.Instance.Update(context);
                        system.ConsecutiveFailures = 0;
                    }
                    catch (Exception ex)
                    {
                        _commands.Clear();
                        system.ConsecutiveFailures++;
                        if (system.ConsecutiveFailures >= MaxConsecutiveFailures)
                            system.Enabled = false;
                        _onError(system, ex);
                    }
                }

                if (!_commands.IsEmpty)
                    _commands.Playback();
            }
        }
    }

    public ScheduledSystem? Find<T>() where T : ISystem => Systems.FirstOrDefault(s => s.Instance is T);

    private static ScheduledSystem[] Order(List<ScheduledSystem> systems, SystemPhase phase)
    {
        var index = systems.Select((s, i) => (s.Descriptor.Type, i)).ToDictionary(x => x.Type, x => x.i);
        var dependents = systems.Select(_ => new List<int>()).ToArray();
        var remaining = new int[systems.Count];
        for (var i = 0; i < systems.Count; i++)
        {
            foreach (var after in systems[i].Descriptor.RunsAfter)
            {
                if (index.TryGetValue(after, out var j))
                {
                    dependents[j].Add(i);
                    remaining[i]++;
                }
            }

            foreach (var before in systems[i].Descriptor.RunsBefore)
            {
                if (index.TryGetValue(before, out var j))
                {
                    dependents[i].Add(j);
                    remaining[j]++;
                }
            }
        }

        var ready = new PriorityQueue<int, (int Order, int Index)>();
        for (var i = 0; i < systems.Count; i++)
        {
            if (remaining[i] == 0)
                ready.Enqueue(i, (systems[i].Descriptor.Order, i));
        }

        var ordered = new List<ScheduledSystem>(systems.Count);
        while (ready.TryDequeue(out var i, out _))
        {
            ordered.Add(systems[i]);
            foreach (var dependent in dependents[i])
            {
                if (--remaining[dependent] == 0)
                    ready.Enqueue(dependent, (systems[dependent].Descriptor.Order, dependent));
            }
        }

        if (ordered.Count != systems.Count)
        {
            var cycle = systems.Where((_, i) => remaining[i] > 0).Select(s => s.Descriptor.Type.Name);
            throw new InvalidOperationException($"The {phase} systems {string.Join(", ", cycle)} depend on each other in a cycle.");
        }

        return [.. ordered];
    }
}
