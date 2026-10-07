using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Advances <see cref="SpriteAnimation"/>s and shows the current frame on the entity's <see cref="Sprite"/>.</summary>
[UpdateIn(SystemPhase.Update)]
[ExecuteIn(ExecutionModes.Preview | ExecutionModes.Play)]
[SystemOrder(100)]
public sealed class SpriteAnimationSystem : ISystem
{
    public void Update(in SystemContext context)
    {
        var job = new AnimateJob(context.Time.DeltaTime);
        context.World.Query<SpriteAnimation, Sprite>().Run<AnimateJob, SpriteAnimation, Sprite>(ref job);
    }

    private readonly struct AnimateJob(float deltaTime) : IForEach<SpriteAnimation, Sprite>
    {
        public void Execute(Entity entity, ref SpriteAnimation animation, ref Sprite sprite)
        {
            var clip = animation.Clip;
            if (animation.Playing)
            {
                animation.Time += deltaTime * animation.Speed;
                if (animation.Time >= clip.Length)
                {
                    if (clip.Loop && clip.Length > 0)
                    {
                        animation.Time %= clip.Length;
                    }
                    else
                    {
                        animation.Time = clip.Length;
                        animation.Playing = false;
                    }
                }
            }

            var time = animation.Time;
            var frames = clip.Frames;
            var index = frames.Count - 1;
            for (var i = 0; i < frames.Count; i++)
            {
                time -= frames[i].Duration;
                if (time < 0)
                {
                    index = i;
                    break;
                }
            }

            sprite.Texture = frames[index].Texture;
            sprite.Source = frames[index].Source;
        }
    }
}
