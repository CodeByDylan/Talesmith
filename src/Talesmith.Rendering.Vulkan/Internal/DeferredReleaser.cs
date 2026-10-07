namespace Talesmith.Rendering.Vulkan.Internal;

/// <summary>Holds GPU resources that frames still in flight may use, and disposes them once those frames complete.</summary>
internal sealed class DeferredReleaser : IDisposable
{
    private readonly Queue<(long Frame, IDisposable Resource)> _pending = new();

    /// <summary>Disposes <paramref name="resource"/> once frame <paramref name="frame"/> has completed on the GPU.</summary>
    public void Release(IDisposable resource, long frame) => _pending.Enqueue((frame, resource));

    public void DisposeCompleted(long completedFrame)
    {
        while (_pending.TryPeek(out var next) && next.Frame <= completedFrame)
            _pending.Dequeue().Resource.Dispose();
    }

    public void Dispose()
    {
        while (_pending.TryDequeue(out var next))
            next.Resource.Dispose();
    }
}
