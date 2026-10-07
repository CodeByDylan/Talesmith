using Talesmith.Runtime.Components;
using Talesmith.Runtime.Systems;
using Talesmith.Systems;

namespace Talesmith.Samples.IsleHopper;

/// <summary>Scrolls maps with a float property "parallax" slower than the camera, so they look far away.</summary>
/// <remarks>
/// A parallax of 1 moves a map with the world and 0 pins it to the screen; the backdrop uses 0.3. Only the horizontal position changes,
/// which keeps the backdrop's horizon level with the sea of the level in front of it. Moving a map makes the engine rebuild the meshes
/// of its visible chunks, which is cheap for a sparse backdrop of large cells.
/// </remarks>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateAfter(typeof(CameraSystem))]
public sealed class ParallaxSystem : ISystem
{
    public const string ParallaxProperty = "parallax";

    public void Update(in SystemContext context)
    {
        if (!context.World.Query<Camera>().TryGetSingle(out var cameraEntity))
            return;

        var camera = context.World.Get<Camera>(cameraEntity).View.Position;
        foreach (var archetype in context.World.Query<TileMapComponent, Transform>())
        {
            var maps = archetype.GetSpan<TileMapComponent>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < maps.Length; i++)
            {
                var parallax = maps[i].Map.Properties.GetFloat(ParallaxProperty, 1);
                if (parallax != 1)
                    transforms[i].Position.X = camera.X * (1 - parallax);
            }
        }
    }
}
