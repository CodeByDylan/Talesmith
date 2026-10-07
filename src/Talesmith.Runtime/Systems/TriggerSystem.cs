using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Raised when a <see cref="TriggerActivator"/> enters a <see cref="TriggerArea"/>.</summary>
public readonly record struct TriggerEntered(Entity Activator, Entity Trigger, TriggerArea Area);

/// <summary>Raised when a <see cref="TriggerActivator"/> leaves a <see cref="TriggerArea"/>.</summary>
public readonly record struct TriggerExited(Entity Activator, Entity Trigger, TriggerArea Area);

/// <summary>Detects activators entering and leaving trigger areas and queues events for them, delivered at the start of the next frame.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
public sealed class TriggerSystem(IEventBus events) : ISystem
{
    private readonly List<(Entity Entity, Transform Transform, TriggerArea Area)> _triggers = [];

    public void Update(in SystemContext context)
    {
        _triggers.Clear();
        var collect = new CollectJob(_triggers);
        context.World.Query<Transform, TriggerArea>().Run<CollectJob, Transform, TriggerArea>(ref collect);
        if (_triggers.Count == 0)
            return;

        var check = new CheckJob(context.World, _triggers, events);
        context.World.Query<Transform, TriggerActivator>().Run<CheckJob, Transform, TriggerActivator>(ref check);
    }

    private readonly struct CollectJob(List<(Entity, Transform, TriggerArea)> triggers) : IForEach<Transform, TriggerArea>
    {
        public void Execute(Entity entity, ref Transform transform, ref TriggerArea area) => triggers.Add((entity, transform, area));
    }

    private readonly struct CheckJob(World world, List<(Entity Entity, Transform Transform, TriggerArea Area)> triggers, IEventBus events)
        : IForEach<Transform, TriggerActivator>
    {
        public void Execute(Entity entity, ref Transform transform, ref TriggerActivator activator)
        {
            var inside = Entity.Null;
            foreach (var (trigger, triggerTransform, area) in triggers)
            {
                if (area.Contains(transform.Position - triggerTransform.Position))
                {
                    inside = trigger;
                    break;
                }
            }

            if (inside == activator.Inside)
                return;
            if (!activator.Inside.IsNull && world.IsAlive(activator.Inside) && world.TryGet<TriggerArea>(activator.Inside, out var left))
                events.Enqueue(new TriggerExited(entity, activator.Inside, left));
            if (!inside.IsNull)
                events.Enqueue(new TriggerEntered(entity, inside, world.Get<TriggerArea>(inside)));
            activator.Inside = inside;
        }
    }
}
