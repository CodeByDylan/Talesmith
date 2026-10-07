using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Rendering.Lighting;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Lighting.Systems;

/// <summary>Fills each frame's <see cref="RenderFrame.Lighting"/> from the scene: lights, glowing sprites, shadow casters and tile map collision layers.</summary>
/// <remarks>
/// Runs in every execution mode, so the editor shows lighting while authoring. Lights are culled against the camera, capped by the
/// <see cref="LightingEnvironment.QualitySettings"/> and ordered by blend mode; occluders are gathered only where shadowed lights reach,
/// and tile map outlines are cached per chunk. Nothing allocates once its buffers have grown.
/// </remarks>
[UpdateIn(SystemPhase.PreRender)]
[SystemOrder(100)]
public sealed class LightingSystem(RenderContext render, LightingEnvironment environment, EngineProfilers profilers) : ISystem, ISystemLifecycle
{
    private readonly LightCollector _lights = new();
    private readonly ShadowCasterCollector _casters = new();
    private readonly TileOccluderCache _tiles = new();

    public void OnStart(World world)
    {
    }

    public void OnStop(World world) => _tiles.Clear();

    public void Update(in SystemContext context)
    {
        if (!environment.Enabled)
            return;

        var profiler = profilers.Game;
        var frame = render.Frame;
        var lighting = frame.Lighting;
        var quality = environment.QualitySettings;
        lighting.Enable(new LightMapSettings(environment.Ambient, environment.LitLayerLimit, quality.ResolutionScale, quality.ShadowResolution,
            quality.ShadowSamples));
        var world = context.World;

        using (profiler.Measure(LightingMarkers.Lights))
        {
            _lights.Collect(world, frame.VisibleBounds, environment.CullingMargin, quality);
            foreach (ref readonly var light in _lights.Lights)
                lighting.AddLight(light);
        }

        using (profiler.Measure(LightingMarkers.Emissive))
        {
            var job = new EmissiveJob(frame, environment.LitLayerLimit);
            world.Query<Transform, Sprite, Emissive>().Run<EmissiveJob, Transform, Sprite, Emissive>(ref job);
            profiler.Increment(LightingMarkers.EmissiveSprites, job.Added);
        }

        profiler.Increment(LightingMarkers.LightsDrawn, _lights.Lights.Length);
        profiler.Increment(LightingMarkers.ShadowedLights, _lights.ShadowedCount);
        if (_lights.ShadowedCount == 0)
            return;

        var region = _lights.ShadowBounds;
        using (profiler.Measure(LightingMarkers.ShadowCasters))
            profiler.Increment(LightingMarkers.ShadowCastersDrawn, _casters.Collect(world, region, frame.VisibleBounds.Center, quality.MaxShadowCasters, lighting));

        if (environment.TileMapShadows)
        {
            using (profiler.Measure(LightingMarkers.TileOccluders))
            {
                var (_, built) = _tiles.Collect(world, region, 1u << Math.Clamp(environment.TileMapShadowLayer, 0, 31), lighting);
                profiler.Increment(LightingMarkers.OccluderChunksBuilt, built);
            }
        }

        profiler.Increment(LightingMarkers.OccluderEdges, lighting.Edges.Length);
    }

    private struct EmissiveJob(RenderFrame frame, int litLayerLimit) : IForEach<Transform, Sprite, Emissive>
    {
        private readonly Rect2 _visible = frame.VisibleBounds;

        public int Added;

        public void Execute(Entity entity, ref Transform transform, ref Sprite sprite, ref Emissive emissive)
        {
            if (!emissive.Enabled || emissive.Intensity <= 0 || !sprite.Visible || sprite.Texture.IsNone || sprite.Layer >= litLayerLimit)
                return;

            var size = sprite.Size * transform.Scale;
            var extent = MathF.Max(MathF.Abs(size.X), MathF.Abs(size.Y));
            if (!_visible.Intersects(Rect2.FromCenter(transform.Position, new Vector2(extent * 2))))
                return;

            var tint = emissive.Color.WithAlpha((byte)(emissive.Color.A * sprite.Tint.A / 255));
            var instance = SpriteInstance.Create(transform.Position, size, sprite.Source, tint, sprite.Origin, transform.Rotation, sprite.FlipX, sprite.FlipY);
            frame.Lighting.AddEmissive(sprite.Texture, instance, emissive.Intensity);
            Added++;
        }
    }
}
