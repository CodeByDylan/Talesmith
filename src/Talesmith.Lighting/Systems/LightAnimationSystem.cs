using Talesmith.Ecs;
using Talesmith.Systems;

namespace Talesmith.Lighting.Systems;

/// <summary>Plays the flicker and pulse animations of <see cref="Light2D"/>s in Preview and Play; in Edit lights hold still.</summary>
[UpdateIn(SystemPhase.Update)]
[ExecuteIn(ExecutionModes.Preview | ExecutionModes.Play)]
public sealed class LightAnimationSystem : ISystem
{
    public void Update(in SystemContext context)
    {
        var job = new AdvanceJob(context.Time.DeltaTime);
        context.World.Query<Light2D>().Run<AdvanceJob, Light2D>(ref job);
    }

    private readonly struct AdvanceJob(float delta) : IForEach<Light2D>
    {
        public void Execute(Entity entity, ref Light2D light)
        {
            if (light.Animation != LightAnimation.None && light.Enabled)
                light.AnimationTime += delta * MathF.Max(0, light.AnimationSpeed);
        }
    }
}
