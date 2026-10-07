using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Events;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;
using Talesmith.VFX.Rendering;

namespace Talesmith.VFX.Systems;

/// <summary>Simulates every <see cref="ParticleEmitter"/> after gameplay has moved them, applying culling, level of detail and the scene budget.</summary>
/// <remarks>
/// Runs in <see cref="SystemPhase.LateUpdate"/>, so emitters follow entities moved during <see cref="SystemPhase.Update"/> in the same
/// frame, and in <see cref="ExecutionModes.Preview"/> as well as <see cref="ExecutionModes.Play"/> so the editor can preview effects.
/// </remarks>
[UpdateIn(SystemPhase.LateUpdate)]
[ExecuteIn(ExecutionModes.Preview | ExecutionModes.Play)]
public sealed class ParticleSimulationSystem(
    RenderContext render,
    ParticleAssets assets,
    ParticleBudget budget,
    ParticleOptions options,
    ParticleEnvironment environment,
    ParticleStats stats,
    EngineProfilers profilers,
    IEventBus events,
    IEnumerable<IParticleCollisionProvider> collisionProviders) : ISystem, ISystemLifecycle
{
    private readonly IParticleCollisionProvider[] _collisionProviders = [.. collisionProviders];

    public void Update(in SystemContext context)
    {
        var profiler = profilers.Game;
        using (profiler.Measure(ParticleCounters.Simulate))
        {
            var query = context.World.Query<Transform, ParticleEmitter>();
            var alive = 0;
            foreach (var archetype in query)
            {
                foreach (var emitter in archetype.GetSpan<ParticleEmitter>())
                    alive += emitter?.AliveCount ?? 0;
            }

            budget.Capacity = options.MaxParticlesPerScene;
            budget.Reset(alive);
            stats.Begin();
            var view = render.VisibleBounds.Inflate(options.CullingMargin);
            var zoom = render.Camera.Zoom;
            var emitted = 0;
            foreach (var archetype in query)
            {
                var entities = archetype.Entities;
                var transforms = archetype.GetSpan<Transform>();
                var emitters = archetype.GetSpan<ParticleEmitter>();
                for (var i = 0; i < entities.Length; i++)
                {
                    if (emitters[i] is { } emitter)
                        emitted += Simulate(context, entities[i], transforms[i], emitter, view, zoom);
                }
            }

            profiler.Set(ParticleCounters.Alive, stats.AliveParticles);
            profiler.Increment(ParticleCounters.Emitted, emitted);
            profiler.Set(ParticleCounters.VisibleEmitters, stats.VisibleEmitters);
            profiler.Set(ParticleCounters.CulledEmitters, stats.CulledEmitters);
        }
    }

    public void OnStart(World world)
    {
    }

    /// <summary>Returns every emitter's buffers to the pool when the scene ends.</summary>
    public void OnStop(World world)
    {
        foreach (var archetype in world.Query<ParticleEmitter>())
        {
            foreach (var emitter in archetype.GetSpan<ParticleEmitter>())
                emitter?.Simulation.Dispose();
        }
    }

    private int Simulate(in SystemContext context, Entity entity, in Transform transform, ParticleEmitter emitter, in Rect2 view, float zoom)
    {
        var settings = assets.GetPreset(emitter.Preset) ?? emitter.Settings;
        var simulation = emitter.Simulation;
        var scale = (MathF.Abs(transform.Scale.X) + MathF.Abs(transform.Scale.Y)) * 0.5f;
        var reach = simulation.ShapeExtent(settings) * MathF.Max(MathF.Abs(transform.Scale.X), MathF.Abs(transform.Scale.Y))
            + settings.Initial.Size.LargestMagnitude() * scale;
        var visible = view.Intersects(Rect2.FromCenter(transform.Position, new Vector2(reach * 2 + 1)))
            || (simulation.AliveCount > 0 && view.Intersects(simulation.Bounds));
        simulation.IsVisible = visible;

        var paused = !visible && settings.Culling switch
        {
            ParticleCullingMode.PauseWhenOffscreen => true,
            ParticleCullingMode.Automatic => settings.Looping,
            _ => false
        };
        if (paused)
        {
            stats.Add(simulation.AliveCount, 0, 0, visible: false);
            return 0;
        }

        var step = new ParticleStepContext(transform.Position, transform.Rotation)
        {
            Scale = transform.Scale,
            Gravity = environment.Gravity,
            EmissionScale = EmissionScale(settings, scale, zoom),
            Budget = budget,
            CollisionProviders = _collisionProviders,
            MaxCollisionEvents = options.MaxCollisionEventsPerEmitter,
            Entity = entity,
            World = context.World
        };
        simulation.Update(settings, step, context.Time.DeltaTime);
        foreach (var collision in simulation.CollisionEvents)
            events.Publish(collision);

        stats.Add(simulation.AliveCount, simulation.EmittedPerSecond, simulation.SimulationMilliseconds, visible);
        return simulation.LastEmitted;
    }

    /// <summary>Scales emission down for quality settings, for particles too small on screen to matter, and as the scene budget fills up.</summary>
    private float EmissionScale(ParticleSettings settings, float emitterScale, float zoom)
    {
        var scale = options.EmissionScale;
        var pixels = settings.Initial.Size.Average() * emitterScale * zoom;
        if (options.LodMinimumPixelSize > 0 && pixels < options.LodMinimumPixelSize)
            scale *= MathF.Max(0.1f, pixels / options.LodMinimumPixelSize);
        var threshold = Math.Clamp(options.BudgetPressureThreshold, 0, 0.999f);
        var pressure = budget.Pressure;
        if (pressure > threshold)
            scale *= MathF.Max(0, (1 - pressure) / (1 - threshold));
        return Math.Clamp(scale, 0, 1);
    }
}
