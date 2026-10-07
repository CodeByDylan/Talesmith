using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Runtime.Tweens;

/// <summary>The default <see cref="ITweenService"/>, advanced once per frame by the game loop.</summary>
public sealed class TweenService : ITweenService
{
    private readonly List<ActiveTween> _tweens = [];
    private readonly List<ActiveTween> _added = [];
    private readonly List<(ActiveTween Tween, bool Canceled, Exception? Error)> _finished = [];

    public int ActiveCount => _tweens.Count + _added.Count;

    public Task Run(float duration, Action<float> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var tween = new ActiveTween(Math.Max(0, duration), apply, easing ?? Easing.Linear, scaled, cancellationToken);
        _added.Add(tween);
        return tween.Source.Task;
    }

    public Task To(float from, float to, float duration, Action<float> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default) =>
        Run(duration, t => apply(MathHelper.Lerp(from, to, t)), easing, scaled, cancellationToken);

    public Task To(Vector2 from, Vector2 to, float duration, Action<Vector2> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default) =>
        Run(duration, t => apply(Vector2.Lerp(from, to, t)), easing, scaled, cancellationToken);

    public Task To(Color from, Color to, float duration, Action<Color> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default) =>
        Run(duration, t => apply(Color.Lerp(from, to, t)), easing, scaled, cancellationToken);

    /// <summary>Advances every tween; called by the game loop on the game thread.</summary>
    /// <remarks>Tasks of finished tweens complete only after every tween has advanced, since their continuations may start or cancel tweens.</remarks>
    public void Update(float scaledDelta, float unscaledDelta)
    {
        _tweens.AddRange(_added);
        _added.Clear();
        for (var i = _tweens.Count - 1; i >= 0; i--)
        {
            var tween = _tweens[i];
            if (tween.Cancellation.IsCancellationRequested)
            {
                _tweens.RemoveAt(i);
                _finished.Add((tween, true, null));
                continue;
            }

            tween.Elapsed += tween.Scaled ? scaledDelta : unscaledDelta;
            var progress = tween.Duration <= 0 ? 1 : Math.Clamp(tween.Elapsed / tween.Duration, 0, 1);
            try
            {
                tween.Apply(tween.Easing(progress));
            }
            catch (Exception ex)
            {
                _tweens.RemoveAt(i);
                _finished.Add((tween, false, ex));
                continue;
            }

            if (progress >= 1)
            {
                _tweens.RemoveAt(i);
                _finished.Add((tween, false, null));
            }
        }

        for (var i = 0; i < _finished.Count; i++)
        {
            var (tween, canceled, error) = _finished[i];
            if (canceled)
                tween.Source.TrySetCanceled(tween.Cancellation);
            else if (error is not null)
                tween.Source.TrySetException(error);
            else
                tween.Source.TrySetResult();
        }

        _finished.Clear();
    }

    /// <summary>Cancels every running tween, for example when a scene unloads.</summary>
    public void CancelAll()
    {
        foreach (var tween in _tweens.Concat(_added))
            tween.Source.TrySetCanceled();
        _tweens.Clear();
        _added.Clear();
    }

    private sealed class ActiveTween(float duration, Action<float> apply, EasingFunction easing, bool scaled, CancellationToken cancellation)
    {
        public readonly TaskCompletionSource Source = new();

        public float Duration { get; } = duration;

        public Action<float> Apply { get; } = apply;

        public EasingFunction Easing { get; } = easing;

        public bool Scaled { get; } = scaled;

        public CancellationToken Cancellation { get; } = cancellation;

        public float Elapsed { get; set; }
    }
}
