using System.Numerics;
using Talesmith.Mathematics;
using Talesmith.Runtime.Tweens;

namespace Talesmith.Scripting;

/// <summary>Animates values over time for scripts; see <see cref="Script.Tweens"/>.</summary>
/// <remarks>Tweens stop when the script is destroyed. Await them to chain animations: <c>await Tweens.To(1f, 0f, 0.3f, a => …);</c></remarks>
public readonly struct ScriptTweens
{
    private readonly Script _script;

    internal ScriptTweens(Script script) => _script = script;

    /// <summary>The tween service, for tweens that should outlive the script.</summary>
    public ITweenService Service => _script.Running.Tweens;

    /// <summary>Calls <paramref name="apply"/> every frame with eased progress from 0 to 1 over <paramref name="duration"/> seconds.</summary>
    /// <param name="scaled">Whether the duration is game time, which follows the time scale, or real time.</param>
    public ScriptTask Run(float duration, Action<float> apply, EasingFunction? easing = null, bool scaled = true) =>
        new(_script, Service.Run(duration, Guard(apply), easing, scaled, _script.DestroyCancellationToken));

    public ScriptTask To(float from, float to, float duration, Action<float> apply, EasingFunction? easing = null, bool scaled = true) =>
        new(_script, Service.To(from, to, duration, Guard(apply), easing, scaled, _script.DestroyCancellationToken));

    public ScriptTask To(Vector2 from, Vector2 to, float duration, Action<Vector2> apply, EasingFunction? easing = null, bool scaled = true) =>
        new(_script, Service.To(from, to, duration, Guard(apply), easing, scaled, _script.DestroyCancellationToken));

    public ScriptTask To(Color from, Color to, float duration, Action<Color> apply, EasingFunction? easing = null, bool scaled = true) =>
        new(_script, Service.To(from, to, duration, Guard(apply), easing, scaled, _script.DestroyCancellationToken));

    private Action<TValue> Guard<TValue>(Action<TValue> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var script = _script;
        return value =>
        {
            if (!script.IsDestroyed)
                script.Running.Guard(script, apply, value);
        };
    }
}
