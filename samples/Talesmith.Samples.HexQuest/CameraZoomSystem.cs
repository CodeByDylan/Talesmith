using Talesmith.Ecs;
using Talesmith.Input;
using Talesmith.Mathematics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Systems;
using Talesmith.Systems;

namespace Talesmith.Samples.HexQuest;

/// <summary>Zooms the camera with the mouse wheel and the zoom keys, easing toward the target zoom.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateBefore(typeof(CameraSystem))]
public sealed class CameraZoomSystem(IInputService input) : ISystem
{
    private const float Step = 1.15f;
    private float _target;

    public void Update(in SystemContext context)
    {
        if (!context.World.Query<Camera>().TryGetSingle(out var entity))
            return;

        ref var camera = ref context.World.Get<Camera>(entity);
        if (_target <= 0)
            _target = camera.View.Zoom;

        var steps = input.WheelDelta.Y;
        if (input.Actions.TryGet("ZoomIn", out var zoomIn) && zoomIn!.WasPressed)
            steps++;
        if (input.Actions.TryGet("ZoomOut", out var zoomOut) && zoomOut!.WasPressed)
            steps--;
        if (steps != 0)
            _target = Math.Clamp(_target * MathF.Pow(Step, steps), 0.35f, 2.5f);

        camera.View.Zoom = MathHelper.Damp(camera.View.Zoom, _target, 12, context.Time.UnscaledDeltaTime);
    }
}
