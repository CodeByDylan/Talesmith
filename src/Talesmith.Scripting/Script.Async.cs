namespace Talesmith.Scripting;

public abstract partial class Script
{
    /// <summary>Waits for <paramref name="seconds"/> of game time, which follows the time scale and stops while the game is paused.</summary>
    /// <remarks>Like every script wait, it resumes on the game thread and never resumes once the script is destroyed.</remarks>
    public ScriptTask Wait(double seconds) => new(this, Running.Scheduler.Delay(seconds, scaled: true, DestroyCancellationToken));

    /// <summary>Waits for <paramref name="seconds"/> of real time, ignoring the time scale and pauses.</summary>
    public ScriptTask WaitRealtime(double seconds) => new(this, Running.Scheduler.Delay(seconds, scaled: false, DestroyCancellationToken));

    /// <summary>Waits until the start of the next frame.</summary>
    public ScriptTask NextFrame() => new(this, Running.Scheduler.NextFrame(DestroyCancellationToken));

    /// <summary>Waits until <paramref name="condition"/> returns true, checking it once per frame.</summary>
    public ScriptTask WaitUntil(Func<bool> condition) => new(this, Running.Scheduler.WaitUntil(condition, DestroyCancellationToken));

    /// <summary>Calls <paramref name="action"/> every <paramref name="interval"/> seconds until the returned handle is disposed or the script is destroyed.</summary>
    /// <param name="scaled">Whether the interval is game time, which follows the time scale, or real time.</param>
    public IDisposable Every(double interval, Action action, bool scaled = true)
    {
        ArgumentNullException.ThrowIfNull(action);
        var script = this;
        return Own(Running.Scheduler.Every(interval, () =>
        {
            if (!script.IsDestroyed)
                script.Running.Guard(script, action);
        }, scaled));
    }

    /// <summary>Starts an asynchronous routine on the game thread, such as a sequence of waits, and logs it if it fails.</summary>
    /// <remarks>Prefer this to <c>async void</c> methods, whose failures cannot be attributed to the script.</remarks>
    /// <example><code>
    /// protected override void OnStart() => Run(async () =>
    /// {
    ///     await Wait(1.5);
    ///     Log.Info("Ready");
    /// });
    /// </code></example>
    public void Run(Func<Task> routine)
    {
        ArgumentNullException.ThrowIfNull(routine);
        _ = Observe(routine());
    }

    internal T Own<T>(T resource) where T : IDisposable
    {
        if (IsDestroyed)
        {
            resource.Dispose();
            return resource;
        }

        return (Lifetime ??= new ScriptLifetime()).Own(resource);
    }

    private async Task Observe(Task routine)
    {
        try
        {
            await routine.ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (IsDestroyed)
        {
        }
        catch (Exception ex)
        {
            Runtime?.ReportRoutineFailure(this, ex);
        }
    }
}
