using Talesmith.Runtime.Systems;

namespace LanternGrove;

/// <summary>Keeps a backdrop far away by moving it with the camera: a factor of 0 leaves it in the world, 1 pins it to the screen.</summary>
[Component(Category = "Rendering", Icon = "layers")]
public struct Parallax
{
    public Vector2 Factor;

    [Transient]
    public Vector2 Home;

    [Transient]
    public Vector2 CameraHome;

    [Transient]
    public bool Started;
}

/// <summary>Moves every <see cref="Parallax"/> entity after the camera has moved, so backdrops never lag a frame behind.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateAfter(typeof(CameraSystem))]
public sealed class ParallaxSystem : ISystem
{
    public void Update(in SystemContext context)
    {
        if (!context.World.Query<Camera>().TryGetSingle(out var cameraEntity))
            return;
        var camera = context.World.Get<Camera>(cameraEntity).View.Position;
        foreach (var archetype in context.World.Query<Parallax, Transform>())
        {
            var layers = archetype.GetSpan<Parallax>();
            var transforms = archetype.GetSpan<Transform>();
            for (var i = 0; i < layers.Length; i++)
            {
                ref var layer = ref layers[i];
                if (!layer.Started)
                {
                    layer.Home = transforms[i].Position;
                    layer.CameraHome = camera;
                    layer.Started = true;
                }

                transforms[i].Position = layer.Home + (camera - layer.CameraHome) * layer.Factor;
            }
        }
    }
}
