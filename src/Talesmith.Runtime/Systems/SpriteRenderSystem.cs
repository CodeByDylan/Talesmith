using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Diagnostics;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Draws every visible <see cref="Sprite"/> at its <see cref="Transform"/>, skipping sprites outside the camera's view.</summary>
/// <remarks>Sprites with <see cref="Sprite.SortByY"/> use their bottom edge as sort key, so lower sprites cover higher ones.</remarks>
[UpdateIn(SystemPhase.PreRender)]
public sealed class SpriteRenderSystem(RenderContext render, EngineProfilers profilers) : ISystem
{
    private static readonly QueryDescription ActiveSprites = QueryDescription.With<Transform>().And<Sprite>().Without<Inactive>();

    public void Update(in SystemContext context)
    {
        var job = new DrawJob(render.Frame);
        context.World.Query(ActiveSprites).Run<DrawJob, Transform, Sprite>(ref job);
        profilers.Game.Increment(RuntimeMarkers.SpritesSubmitted, job.Drawn);
    }

    private struct DrawJob(RenderFrame frame) : IForEach<Transform, Sprite>
    {
        private readonly Rect2 _visible = frame.VisibleBounds;

        public int Drawn;

        public void Execute(Entity entity, ref Transform transform, ref Sprite sprite)
        {
            if (!sprite.Visible || sprite.Texture.IsNone)
                return;

            var size = sprite.Size * transform.Scale;
            var extent = MathF.Max(MathF.Abs(size.X), MathF.Abs(size.Y));
            if (!_visible.Intersects(Rect2.FromCenter(transform.Position, new Vector2(extent * 2))))
                return;

            var instance = SpriteInstance.Create(transform.Position, size, sprite.Source, sprite.Tint, sprite.Origin, transform.Rotation, sprite.FlipX, sprite.FlipY);
            var sortKey = sprite.SortByY ? transform.Position.Y + (1 - sprite.Origin.Y) * size.Y : 0;
            frame.Draw(sprite.Texture, sprite.Material, instance, sprite.Layer, sortKey);
            Drawn++;
        }
    }
}
