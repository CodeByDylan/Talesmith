namespace Talesmith.Audio.Decoding;

internal static class SampleConversion
{
    public static short ToInt16(float sample) => (short)Math.Clamp(MathF.Round(sample * short.MaxValue), short.MinValue, short.MaxValue);
}
