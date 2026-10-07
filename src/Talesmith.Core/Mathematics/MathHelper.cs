namespace Talesmith.Mathematics;

/// <summary>Small numeric helpers that <see cref="Math"/> does not provide.</summary>
public static class MathHelper
{
    public static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    /// <summary>Moves <paramref name="current"/> toward <paramref name="target"/> by at most <paramref name="maxDelta"/>.</summary>
    public static float MoveTowards(float current, float target, float maxDelta) =>
        MathF.Abs(target - current) <= maxDelta ? target : current + MathF.Sign(target - current) * maxDelta;

    /// <summary>Exponential smoothing that behaves the same at any frame rate; higher <paramref name="sharpness"/> converges faster.</summary>
    public static float Damp(float current, float target, float sharpness, float deltaTime) =>
        Lerp(current, target, 1 - MathF.Exp(-sharpness * deltaTime));

    public static float ToRadians(float degrees) => degrees * (MathF.PI / 180f);

    public static float ToDegrees(float radians) => radians * (180f / MathF.PI);
}
