namespace Talesmith.Scripting;

/// <summary>Where a script is in its lifecycle.</summary>
[Flags]
internal enum ScriptState
{
    None = 0,

    /// <summary>Waiting for <see cref="ScriptCallback.Create"/>.</summary>
    Pending = 1,
    Created = 2,
    Started = 4,

    /// <summary><see cref="ScriptCallback.Enable"/> ran and <see cref="ScriptCallback.Disable"/> has not since.</summary>
    Active = 8,
    Faulted = 16,
    Destroyed = 32
}

/// <summary>The overridable methods of <see cref="Script"/>.</summary>
internal enum ScriptCallback
{
    Create,
    Start,
    Enable,
    Disable,
    FixedUpdate,
    Update,
    LateUpdate,
    Destroy,
    CollisionEnter,
    CollisionStay,
    CollisionExit,
    TriggerEnter,
    TriggerStay,
    TriggerExit,
    SceneLoaded,
    SceneUnloaded
}

/// <summary>What a script owns until it is destroyed: a cancellation token for its waits and tweens, and its subscriptions.</summary>
internal sealed class ScriptLifetime
{
    private CancellationTokenSource? _cancellation;
    private List<IDisposable>? _owned;

    public CancellationToken Token => (_cancellation ??= new CancellationTokenSource()).Token;

    public T Own<T>(T resource) where T : IDisposable
    {
        (_owned ??= []).Add(resource);
        return resource;
    }

    public void End()
    {
        _cancellation?.Cancel();
        if (_owned is null)
            return;
        foreach (var resource in _owned)
            resource.Dispose();
        _owned.Clear();
    }
}
