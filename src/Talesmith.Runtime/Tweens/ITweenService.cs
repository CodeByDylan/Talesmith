using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Runtime.Tweens;

/// <summary>Animates values over game time, for camera moves, fades and other smooth transitions.</summary>
/// <remarks>The returned task completes on the game thread when the tween ends, so tweens can be chained with <c>await</c>.</remarks>
public interface ITweenService
{
    /// <summary>Calls <paramref name="apply"/> each frame with eased progress from 0 to 1 over <paramref name="duration"/> seconds.</summary>
    Task Run(float duration, Action<float> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default);

    Task To(float from, float to, float duration, Action<float> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default);

    Task To(Vector2 from, Vector2 to, float duration, Action<Vector2> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default);

    Task To(Color from, Color to, float duration, Action<Color> apply, EasingFunction? easing = null, bool scaled = true, CancellationToken cancellationToken = default);
}
