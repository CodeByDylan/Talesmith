using System.Numerics;
using Talesmith.Assets;
using Talesmith.Audio;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scheduling;
using Talesmith.Runtime.Tweens;

namespace Talesmith.Samples.Cutscenes;

/// <summary>Plays cutscene scripts: takes control from the player, runs the steps and gives control back.</summary>
public sealed class CutscenePlayer(DialogueState dialogue, PlayerControl control, ITweenService tweens, IGameScheduler scheduler, IAssetManager assets,
    IAudioService audio)
{
    public bool IsPlaying { get; private set; }

    /// <summary>Plays a script in a world; returns when it ends.</summary>
    /// <exception cref="InvalidOperationException">A cutscene is already playing.</exception>
    public async Task PlayAsync(CutsceneScript script, World world, CancellationToken cancellationToken = default)
    {
        if (IsPlaying)
            throw new InvalidOperationException("A cutscene is already playing.");

        IsPlaying = true;
        using var suspension = control.Suspend("cutscene");
        var camera = new CameraDirector(world, tweens);
        try
        {
            var index = 0;
            while (index < script.Steps.Count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index = await RunAsync(script, script.Steps[index], index, world, camera, cancellationToken);
            }
        }
        finally
        {
            dialogue.Close();
            camera.Release();
            IsPlaying = false;
        }
    }

    private async Task<int> RunAsync(CutsceneScript script, CutsceneStep step, int index, World world, CameraDirector camera, CancellationToken cancellationToken)
    {
        switch (step)
        {
            case SayStep say:
                await dialogue.SayAsync(say.Speaker, say.Text);
                break;
            case ChoiceStep choice:
                var chosen = await dialogue.ChooseAsync(choice.Speaker, choice.Text, choice.Options.Select(o => o.Text).ToList());
                dialogue.Close();
                return JumpTo(script, choice.Options[chosen].Goto);
            case CameraStep move:
                dialogue.Close();
                if (FindObject(world, move.Target) is { } target)
                    await camera.PanToAsync(target, move.Duration, cancellationToken);
                break;
            case CameraBackStep back:
                dialogue.Close();
                await camera.ReturnAsync(back.Duration, cancellationToken);
                break;
            case WaitStep wait:
                await scheduler.Delay(wait.Seconds, cancellationToken: cancellationToken);
                break;
            case SoundStep sound:
                audio.Play(await assets.LoadAsync<SoundClip>(sound.Path, cancellationToken), new SoundOptions(Volume: sound.Volume));
                break;
            case GotoStep jump:
                return JumpTo(script, jump.Label);
            case EndStep:
                return script.Steps.Count;
        }

        return index + 1;
    }

    private static int JumpTo(CutsceneScript script, string label) =>
        script.Labels.TryGetValue(label, out var target) ? target : throw new InvalidOperationException($"The cutscene has no label '{label}'.");

    private static Vector2? FindObject(World world, string name)
    {
        foreach (var archetype in world.Query<Transform, MapObjectComponent>())
        {
            var objects = archetype.GetSpan<MapObjectComponent>();
            for (var i = 0; i < objects.Length; i++)
            {
                if (string.Equals(objects[i].Object.Name, name, StringComparison.OrdinalIgnoreCase))
                    return archetype.GetSpan<Transform>()[i].Position;
            }
        }

        return null;
    }

    /// <summary>Takes the main camera off its target during a cutscene and restores it afterwards.</summary>
    private sealed class CameraDirector(World world, ITweenService tweens)
    {
        private Entity _camera;
        private Entity _originalTarget;
        private bool _taken;

        public async Task PanToAsync(Vector2 position, float duration, CancellationToken cancellationToken)
        {
            if (!Take())
                return;
            var from = world.Get<Camera>(_camera).View.Position;
            await tweens.To(from, position, duration, Move, Easing.CubicInOut, cancellationToken: cancellationToken);
        }

        public async Task ReturnAsync(float duration, CancellationToken cancellationToken)
        {
            if (!_taken || !world.IsAlive(_camera))
                return;
            if (world.IsAlive(_originalTarget) && world.TryGet<Transform>(_originalTarget, out var target))
                await tweens.To(world.Get<Camera>(_camera).View.Position, target.Position, duration, Move, Easing.CubicInOut, cancellationToken: cancellationToken);
            Release();
        }

        public void Release()
        {
            if (_taken && world.IsAlive(_camera))
                world.Get<Camera>(_camera).Target = _originalTarget;
            _taken = false;
        }

        private bool Take()
        {
            if (_taken)
                return world.IsAlive(_camera);
            if (!world.Query<Camera>().TryGetSingle(out _camera))
                return false;
            ref var camera = ref world.Get<Camera>(_camera);
            _originalTarget = camera.Target;
            camera.Target = Entity.Null;
            _taken = true;
            return true;
        }

        private void Move(Vector2 position)
        {
            if (world.IsAlive(_camera))
                world.Get<Camera>(_camera).View.Position = position;
        }
    }
}
