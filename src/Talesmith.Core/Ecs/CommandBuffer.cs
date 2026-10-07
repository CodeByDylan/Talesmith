namespace Talesmith.Ecs;

/// <summary>Records structural changes so they can be applied after a query finishes iterating.</summary>
/// <remarks>
/// Entities created through the buffer are reserved immediately, so their handles can be stored and given components right away;
/// they become visible to queries when the buffer is played back. Component values are kept in typed lists, so recording does not box.
/// </remarks>
public sealed class CommandBuffer(World world)
{
    private readonly List<Command> _commands = [];
    private readonly Dictionary<int, IPayloads> _payloads = new();

    public World World { get; } = world;

    public bool IsEmpty => _commands.Count == 0;

    /// <summary>Reserves a new entity that joins the world on playback.</summary>
    public Entity Create()
    {
        var entity = World.Reserve();
        _commands.Add(new Command(CommandKind.Create, entity, 0, 0));
        return entity;
    }

    public void Destroy(Entity entity) => _commands.Add(new Command(CommandKind.Destroy, entity, 0, 0));

    /// <summary>Adds or replaces a component on playback.</summary>
    public void Set<T>(Entity entity, in T component)
    {
        var typeId = ComponentTypeCache<T>.Id;
        if (!_payloads.TryGetValue(typeId, out var payloads))
            _payloads[typeId] = payloads = new Payloads<T>();
        var list = (Payloads<T>)payloads;
        list.Values.Add(component);
        _commands.Add(new Command(CommandKind.Set, entity, typeId, list.Values.Count - 1));
    }

    public void Remove<T>(Entity entity) => _commands.Add(new Command(CommandKind.Remove, entity, ComponentTypeCache<T>.Id, 0));

    /// <summary>Applies the recorded changes in order and clears the buffer. Changes to entities that died meanwhile are skipped.</summary>
    public void Playback()
    {
        foreach (var command in _commands)
        {
            if (!World.IsAlive(command.Entity))
                continue;

            switch (command.Kind)
            {
                case CommandKind.Create:
                    break;
                case CommandKind.Destroy:
                    World.Destroy(command.Entity);
                    break;
                case CommandKind.Set:
                    _payloads[command.TypeId].Apply(World, command.Entity, command.PayloadIndex);
                    break;
                case CommandKind.Remove:
                    World.Remove(command.Entity, command.TypeId);
                    break;
            }
        }

        Clear();
    }

    /// <summary>Discards recorded changes. Reserved entities stay alive without components.</summary>
    public void Clear()
    {
        _commands.Clear();
        foreach (var payloads in _payloads.Values)
            payloads.Clear();
    }

    private enum CommandKind : byte
    {
        Create,
        Destroy,
        Set,
        Remove
    }

    private readonly record struct Command(CommandKind Kind, Entity Entity, int TypeId, int PayloadIndex);

    private interface IPayloads
    {
        void Apply(World world, Entity entity, int index);

        void Clear();
    }

    private sealed class Payloads<T> : IPayloads
    {
        public readonly List<T> Values = [];

        public void Apply(World world, Entity entity, int index) => world.Set(entity, Values[index]);

        public void Clear() => Values.Clear();
    }
}
