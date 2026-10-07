namespace Talesmith.Avalonia.Presentation;

/// <summary>Serializes drawing with Skia renderers on Avalonia's render thread and releases their GPU resources there.</summary>
/// <remarks>Skia's GPU context is single-threaded; when nothing draws any more, released renderers are disposed under the gate after a delay.</remarks>
public static class SkiaRenderThread
{
    private static readonly TimeSpan FallbackDelay = TimeSpan.FromSeconds(5);
    private static readonly Queue<IDisposable> Pending = new();

    /// <summary>Held while a Skia renderer draws or is disposed.</summary>
    public static Lock Gate { get; } = new();

    /// <summary>Disposes a renderer on the render thread once no draw uses it.</summary>
    public static void Release(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (Gate)
            Pending.Enqueue(resource);
        _ = Task.Delay(FallbackDelay).ContinueWith(_ => Drain(), TaskScheduler.Default);
    }

    /// <summary>Disposes the released renderers; call on the render thread while drawing, or under no lease after the fallback delay.</summary>
    public static void Drain()
    {
        lock (Gate)
        {
            while (Pending.TryDequeue(out var resource))
                resource.Dispose();
        }
    }
}
