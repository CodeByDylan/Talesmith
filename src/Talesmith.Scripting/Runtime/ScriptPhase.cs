using Talesmith.Diagnostics;

namespace Talesmith.Scripting;

/// <summary>The scripts that override one update method, grouped by type in run order, so each type is timed once per phase.</summary>
internal sealed class ScriptPhase(ScriptCallback callback)
{
    private static readonly Predicate<Script> IsDestroyed = static s => s.IsDestroyed;
    private static readonly Comparison<Bucket> RunOrder = static (a, b) =>
        a.Info.Order != b.Info.Order ? a.Info.Order.CompareTo(b.Info.Order) : a.Info.Index.CompareTo(b.Info.Index);

    private readonly List<Bucket> _buckets = [];
    private readonly Dictionary<ScriptTypeInfo, Bucket> _byType = new();
    private bool _unsorted;
    private bool _dirty;

    public void Add(Script script)
    {
        var info = script.Info!;
        if (!_byType.TryGetValue(info, out var bucket))
        {
            bucket = new Bucket(info);
            _byType[info] = bucket;
            _buckets.Add(bucket);
            _unsorted = true;
        }

        bucket.Scripts.Add(script);
    }

    /// <summary>Notes that a script was destroyed, so the buckets are compacted after the next run.</summary>
    public void MarkDirty() => _dirty = true;

    public void Clear()
    {
        _buckets.Clear();
        _byType.Clear();
        _dirty = false;
    }

    public void Run(ScriptRuntime runtime, Profiler profiler)
    {
        if (_unsorted)
        {
            _buckets.Sort(RunOrder);
            _unsorted = false;
        }

        var count = _buckets.Count;
        for (var b = 0; b < count; b++)
        {
            var bucket = _buckets[b];
            var scripts = bucket.Scripts;
            var length = scripts.Count;
            if (length == 0)
                continue;
            using (profiler.Measure(bucket.Info.Marker))
            {
                for (var i = 0; i < length; i++)
                {
                    var script = scripts[i];
                    if ((script.State & (ScriptState.Active | ScriptState.Started | ScriptState.Destroyed)) != (ScriptState.Active | ScriptState.Started))
                        continue;
                    try
                    {
                        script.Invoke(callback);
                        script.Failures = 0;
                    }
                    catch (Exception ex)
                    {
                        runtime.Fail(script, callback, ex);
                    }
                }
            }
        }

        if (!_dirty)
            return;
        foreach (var bucket in _buckets)
            bucket.Scripts.RemoveAll(IsDestroyed);
        _dirty = false;
    }

    private sealed class Bucket(ScriptTypeInfo info)
    {
        public ScriptTypeInfo Info { get; } = info;

        public List<Script> Scripts { get; } = [];
    }
}
