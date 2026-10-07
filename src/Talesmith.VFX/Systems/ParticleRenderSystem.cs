using System.Numerics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;
using Talesmith.VFX.Rendering;

namespace Talesmith.VFX.Systems;

/// <summary>Draws each visible emitter's particles as one span of sprites, so an emitter costs one batch however many particles it has.</summary>
/// <remarks>Runs in every execution mode, so particles stay visible when the editor pauses a preview.</remarks>
[UpdateIn(SystemPhase.PreRender)]
public sealed class ParticleRenderSystem(RenderContext render, ParticleAssets assets, EngineProfilers profilers) : ISystem
{
    private SpriteInstance[] _instances = new SpriteInstance[1024];

    public void Update(in SystemContext context)
    {
        var profiler = profilers.Game;
        using (profiler.Measure(ParticleCounters.Draw))
        {
            var frame = render.Frame;
            var drawn = 0;
            foreach (var archetype in context.World.Query<Transform, ParticleEmitter>())
            {
                var transforms = archetype.GetSpan<Transform>();
                var emitters = archetype.GetSpan<ParticleEmitter>();
                for (var i = 0; i < emitters.Length; i++)
                {
                    if (emitters[i] is { } emitter)
                        drawn += Draw(frame, transforms[i], emitter.Simulation);
                }
            }

            profiler.Increment(ParticleCounters.Drawn, drawn);
        }
    }

    private int Draw(RenderFrame frame, in Transform transform, ParticleSimulation simulation)
    {
        var settings = simulation.Settings;
        var count = simulation.AliveCount;
        if (settings is null || count == 0 || !settings.Renderer.Enabled || !frame.IsVisible(simulation.Bounds))
            return 0;

        if (_instances.Length < count)
            _instances = new SpriteInstance[Math.Max(count, _instances.Length * 2)];
        var renderer = settings.Renderer;
        var region = assets.GetTexture(renderer);
        var localToWorld = Matrix3x2.CreateScale(transform.Scale) * Matrix3x2.CreateRotation(transform.Rotation)
            * Matrix3x2.CreateTranslation(transform.Position);
        var written = simulation.WriteInstances(_instances, region.Source, localToWorld);
        if (written == 0)
            return 0;
        var sortKey = renderer.SortOrder + (renderer.SortByEmitterY ? transform.Position.Y : 0);
        frame.Draw(region.Texture, ParticleAssets.MaterialFor(renderer.Blend), _instances.AsSpan(0, written), renderer.Layer, sortKey);
        return written;
    }
}
