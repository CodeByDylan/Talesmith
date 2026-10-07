using SkiaSharp;

namespace Talesmith.Rendering.Skia;

/// <summary>Keeps objects bound to a GPU context alive until a render on that context can release them on its own thread.</summary>
/// <remarks>A GPU context is not thread-safe, so its surfaces and snapshots must not be freed while its render thread uses it.</remarks>
internal static class GpuReleaseQueue
{
    private static readonly Lock Gate = new();
    private static readonly List<(nint Context, IDisposable Owner)> Pending = [];

    public static void Enqueue(GRRecordingContext context, IDisposable owner)
    {
        lock (Gate)
            Pending.Add((context.Handle, owner));
    }

    /// <summary>Releases what waits for <paramref name="context"/>; call it on the thread that renders with the context.</summary>
    public static void Drain(GRRecordingContext? context)
    {
        if (context is null)
            return;
        List<IDisposable>? release = null;
        lock (Gate)
        {
            if (Pending.Count == 0)
                return;
            for (var i = Pending.Count - 1; i >= 0; i--)
            {
                if (Pending[i].Context != context.Handle)
                    continue;
                (release ??= []).Add(Pending[i].Owner);
                Pending.RemoveAt(i);
            }
        }

        if (release is null)
            return;
        foreach (var owner in release)
            owner.Dispose();
    }
}
