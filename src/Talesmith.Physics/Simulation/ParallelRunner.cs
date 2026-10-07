namespace Talesmith.Physics.Simulation;

/// <summary>A range of independent work items that a <see cref="ParallelRunner"/> splits across threads.</summary>
internal interface IRangeJob
{
    void Execute(int start, int end);
}

/// <summary>Runs independent work items on the thread pool and the calling thread together, without allocating.</summary>
/// <remarks>
/// Items are handed out in fixed-size blocks, so each item is computed by exactly one thread and results written per item do not depend
/// on scheduling; callers keep determinism by consuming results in item order afterwards.
/// </remarks>
internal sealed class ParallelRunner : IDisposable
{
    private const int BlockSize = 64;

    private readonly Worker[] _workers;
    private readonly ManualResetEventSlim _done = new(false);
    private IRangeJob? _job;
    private int _count;
    private int _next;
    private int _pending;

    public ParallelRunner(int workers = -1)
    {
        var count = workers >= 0 ? workers : Math.Clamp(Environment.ProcessorCount - 1, 0, 7);
        _workers = new Worker[count];
        for (var i = 0; i < count; i++)
            _workers[i] = new Worker(this);
    }

    /// <summary>Calls <paramref name="job"/> for every block of the range <c>[0, count)</c> and returns when all are done.</summary>
    public void For(int count, IRangeJob job)
    {
        var helpers = Math.Min(_workers.Length, count / BlockSize - 1);
        if (helpers <= 0)
        {
            job.Execute(0, count);
            return;
        }

        _job = job;
        _count = count;
        _next = 0;
        _pending = helpers;
        _done.Reset();
        for (var i = 0; i < helpers; i++)
            ThreadPool.UnsafeQueueUserWorkItem(_workers[i], preferLocal: false);
        Work();
        if (Volatile.Read(ref _pending) > 0)
            _done.Wait();
        _job = null;
    }

    private void Work()
    {
        var job = _job!;
        while (true)
        {
            var start = Interlocked.Add(ref _next, BlockSize) - BlockSize;
            if (start >= _count)
                return;
            job.Execute(start, Math.Min(start + BlockSize, _count));
        }
    }

    public void Dispose() => _done.Dispose();

    private sealed class Worker(ParallelRunner runner) : IThreadPoolWorkItem
    {
        public void Execute()
        {
            runner.Work();
            if (Interlocked.Decrement(ref runner._pending) == 0)
                runner._done.Set();
        }
    }
}
