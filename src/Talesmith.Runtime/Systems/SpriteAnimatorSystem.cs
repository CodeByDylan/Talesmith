using System.Numerics;
using Talesmith.Assets.Textures;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Rendering;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Turns each <see cref="SpriteAnimator"/>'s named animation into the <see cref="SpriteAnimation"/> that plays it, and keeps speed and playing state in sync.</summary>
/// <remarks>
/// Runs in every mode, so the editor shows an animation's first frame while authoring; <see cref="SpriteAnimationSystem"/> advances it in
/// Preview and Play. Clips are built once per texture and animation name. Entities without a <see cref="Sprite"/> get one.
/// </remarks>
[UpdateIn(SystemPhase.Update)]
[ExecuteIn(ExecutionModes.All)]
[UpdateBefore(typeof(SpriteAnimationSystem))]
public sealed class SpriteAnimatorSystem(TextureCache textures) : ISystem
{
    private readonly Dictionary<TextureAsset, Dictionary<string, AnimationClip?>> _clips = new(ReferenceEqualityComparer.Instance);

    public void Update(in SystemContext context)
    {
        var job = new SyncJob(this, context.World, context.Commands);
        context.World.Query<SpriteAnimator>().Run<SyncJob, SpriteAnimator>(ref job);
    }

    /// <summary>Gets the clip of a texture's animation, or null when the texture has no such animation or its frames name no sprites.</summary>
    public AnimationClip? GetClip(TextureAsset texture, string animation)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(animation);
        if (!_clips.TryGetValue(texture, out var byName))
            _clips[texture] = byName = new Dictionary<string, AnimationClip?>(StringComparer.Ordinal);
        if (byName.TryGetValue(animation, out var clip))
            return clip;

        if (texture.FindAnimation(animation) is { } info)
        {
            var handle = textures.Get(texture);
            var duration = info.FramesPerSecond > 0 ? 1 / info.FramesPerSecond : 0.1f;
            var frames = info.Frames.Select(texture.FindSprite).OfType<SpriteSlice>().Select(s => new AnimationFrame(handle, s.Rect, duration)).ToList();
            if (frames.Count > 0)
                clip = new AnimationClip(info.Name, frames, info.Loop);
        }

        return byName[animation] = clip;
    }

    private readonly struct SyncJob(SpriteAnimatorSystem system, World world, CommandBuffer commands) : IForEach<SpriteAnimator>
    {
        public void Execute(Entity entity, ref SpriteAnimator animator)
        {
            if (!animator.Bound || !ReferenceEquals(animator.BoundTexture, animator.Texture) || animator.BoundAnimation != animator.Animation)
                Bind(entity, ref animator);

            ref var animation = ref world.TryGetRef<SpriteAnimation>(entity, out var animated);
            if (!animated)
                return;
            animation.Speed = animator.Speed;
            if (animator.Playing != animator.BoundPlaying)
            {
                if (animator.Playing && !animation.Clip.Loop && animation.Time >= animation.Clip.Length)
                    animation.Time = 0;
                animation.Playing = animator.Playing;
            }

            animator.Playing = animator.BoundPlaying = animation.Playing;
        }

        private void Bind(Entity entity, ref SpriteAnimator animator)
        {
            animator.Bound = true;
            animator.BoundTexture = animator.Texture;
            animator.BoundAnimation = animator.Animation;
            animator.Playing = animator.BoundPlaying = animator.PlayOnStart;

            var clip = animator.Texture is { } texture && !string.IsNullOrEmpty(animator.Animation) ? system.GetClip(texture, animator.Animation) : null;
            ref var animation = ref world.TryGetRef<SpriteAnimation>(entity, out var animated);
            if (clip is null)
            {
                if (animated)
                    commands.Remove<SpriteAnimation>(entity);
                return;
            }

            if (animated)
            {
                animation.Clip = clip;
                animation.Time = 0;
                animation.Playing = animator.PlayOnStart;
                animation.Speed = animator.Speed;
            }
            else
            {
                commands.Set(entity, new SpriteAnimation(clip) { Playing = animator.PlayOnStart, Speed = animator.Speed });
            }

            var first = clip.Frames[0];
            ref var sprite = ref world.TryGetRef<Sprite>(entity, out var hasSprite);
            if (!hasSprite)
            {
                commands.Set(entity, new Sprite(first.Texture) { Source = first.Source, Size = first.Source.Size });
                return;
            }

            if (sprite.Texture.IsNone || sprite.Size == Vector2.Zero)
                sprite.Size = first.Source.Size;
            sprite.Texture = first.Texture;
            sprite.Source = first.Source;
        }
    }
}
