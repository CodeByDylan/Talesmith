using System.Runtime.CompilerServices;

namespace Talesmith.Scripting;

/// <summary>Asynchronous work started by a script: awaiting it resumes on the game thread, and never resumes once the script is destroyed.</summary>
/// <remarks>So a routine such as <c>await Wait(2); Destroy();</c> simply stops when its script goes away, like a coroutine.</remarks>
public readonly struct ScriptTask
{
    private readonly Script _script;
    private readonly Task _task;

    internal ScriptTask(Script script, Task task)
    {
        _script = script;
        _task = task;
    }

    /// <summary>The underlying task, for combinators such as <see cref="Task.WhenAll(Task[])"/>; awaiting it directly resumes even after the script was destroyed.</summary>
    public Task AsTask() => _task;

    public Awaiter GetAwaiter() => new(_script, _task);

    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        private readonly Script _script;
        private readonly Task _task;

        internal Awaiter(Script script, Task task)
        {
            _script = script;
            _task = task;
        }

        public bool IsCompleted => _task.IsCompleted && !_script.IsDestroyed;

        public void GetResult() => _task.GetAwaiter().GetResult();

        public void OnCompleted(Action continuation) => _task.GetAwaiter().OnCompleted(ScriptContinuation.Guard(_script, continuation));

        public void UnsafeOnCompleted(Action continuation) => _task.GetAwaiter().UnsafeOnCompleted(ScriptContinuation.Guard(_script, continuation));
    }
}

/// <summary>Asynchronous work with a result started by a script; see <see cref="ScriptTask"/>.</summary>
public readonly struct ScriptTask<T>
{
    private readonly Script _script;
    private readonly Task<T> _task;

    internal ScriptTask(Script script, Task<T> task)
    {
        _script = script;
        _task = task;
    }

    /// <summary>The underlying task; awaiting it directly resumes even after the script was destroyed.</summary>
    public Task<T> AsTask() => _task;

    public Awaiter GetAwaiter() => new(_script, _task);

    public readonly struct Awaiter : ICriticalNotifyCompletion
    {
        private readonly Script _script;
        private readonly Task<T> _task;

        internal Awaiter(Script script, Task<T> task)
        {
            _script = script;
            _task = task;
        }

        public bool IsCompleted => _task.IsCompleted && !_script.IsDestroyed;

        public T GetResult() => _task.GetAwaiter().GetResult();

        public void OnCompleted(Action continuation) => _task.GetAwaiter().OnCompleted(ScriptContinuation.Guard(_script, continuation));

        public void UnsafeOnCompleted(Action continuation) => _task.GetAwaiter().UnsafeOnCompleted(ScriptContinuation.Guard(_script, continuation));
    }
}

internal static class ScriptContinuation
{
    public static Action Guard(Script script, Action continuation) => () =>
    {
        if (!script.IsDestroyed)
            continuation();
    };
}
