using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Moves cameras toward their targets, limits their zoom and keeps their view inside their bounds.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
public sealed class CameraSystem(Viewport viewport) : ISystem
{
    public void Update(in SystemContext context)
    {
        var job = new FollowJob(context.World, context.Time.UnscaledDeltaTime, viewport.Layout.ViewSize);
        context.World.Query<Camera>().Run<FollowJob, Camera>(ref job);
    }

    private readonly struct FollowJob(World world, float deltaTime, Vector2 viewSize) : IForEach<Camera>
    {
        public void Execute(Entity entity, ref Camera camera)
        {
            camera.View.Zoom = Math.Clamp(camera.View.Zoom, camera.MinZoom, camera.MaxZoom);
            if (!camera.Target.IsNull && world.IsAlive(camera.Target) && world.TryGet<Transform>(camera.Target, out var target))
            {
                camera.View.Position = camera.FollowSharpness <= 0
                    ? target.Position
                    : new Vector2(
                        MathHelper.Damp(camera.View.Position.X, target.Position.X, camera.FollowSharpness, deltaTime),
                        MathHelper.Damp(camera.View.Position.Y, target.Position.Y, camera.FollowSharpness, deltaTime));
            }

            if (camera.Bounds is { } bounds)
                camera.View.Position = Clamp(camera.View.Position, bounds, viewSize / camera.View.Zoom);
        }

        private static Vector2 Clamp(Vector2 position, Rect2 bounds, Vector2 visibleSize)
        {
            var half = visibleSize / 2;
            var x = visibleSize.X >= bounds.Width ? bounds.Center.X : Math.Clamp(position.X, bounds.Left + half.X, bounds.Right - half.X);
            var y = visibleSize.Y >= bounds.Height ? bounds.Center.Y : Math.Clamp(position.Y, bounds.Top + half.Y, bounds.Bottom - half.Y);
            return new Vector2(x, y);
        }
    }
}
