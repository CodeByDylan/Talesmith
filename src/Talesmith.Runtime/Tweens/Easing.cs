namespace Talesmith.Runtime.Tweens;

/// <summary>Maps linear progress from 0 to 1 onto an eased curve.</summary>
public delegate float EasingFunction(float t);

/// <summary>Common easing curves.</summary>
public static class Easing
{
    public static readonly EasingFunction Linear = t => t;
    public static readonly EasingFunction QuadIn = t => t * t;
    public static readonly EasingFunction QuadOut = t => t * (2 - t);
    public static readonly EasingFunction QuadInOut = t => t < 0.5f ? 2 * t * t : -1 + (4 - 2 * t) * t;
    public static readonly EasingFunction CubicIn = t => t * t * t;
    public static readonly EasingFunction CubicOut = t => 1 - MathF.Pow(1 - t, 3);
    public static readonly EasingFunction CubicInOut = t => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
    public static readonly EasingFunction SineInOut = t => -(MathF.Cos(MathF.PI * t) - 1) / 2;

    /// <summary>Overshoots slightly before settling, for a lively arrival.</summary>
    public static readonly EasingFunction BackOut = t =>
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    };
}
