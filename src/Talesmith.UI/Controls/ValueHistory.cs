namespace Talesmith.UI.Controls;

/// <summary>A fixed-capacity ring buffer of samples, ordered from oldest to newest.</summary>
public sealed class ValueHistory
{
    private readonly double[] _values;
    private int _start;

    public ValueHistory(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _values = new double[capacity];
    }

    /// <summary>Raised after a sample is added or the history is cleared.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the maximum number of samples retained.</summary>
    public int Capacity => _values.Length;

    /// <summary>Gets the number of samples currently retained.</summary>
    public int Count { get; private set; }

    /// <summary>Gets the newest sample, or zero when empty.</summary>
    public double Latest => Count == 0 ? 0 : this[Count - 1];

    /// <summary>Gets the largest retained sample, or zero when empty.</summary>
    public double Max => Aggregate(double.MinValue, Math.Max);

    /// <summary>Gets the smallest retained sample, or zero when empty.</summary>
    public double Min => Aggregate(double.MaxValue, Math.Min);

    /// <summary>Gets the mean of the retained samples, or zero when empty.</summary>
    public double Average => Count == 0 ? 0 : Aggregate(0, static (sum, v) => sum + v) / Count;

    /// <summary>Gets the sample at <paramref name="index"/>, where zero is the oldest.</summary>
    public double this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return _values[(_start + index) % _values.Length];
        }
    }

    /// <summary>Appends a sample, discarding the oldest one when full.</summary>
    public void Push(double value)
    {
        if (Count < _values.Length)
        {
            _values[(_start + Count) % _values.Length] = value;
            Count++;
        }
        else
        {
            _values[_start] = value;
            _start = (_start + 1) % _values.Length;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes all samples.</summary>
    public void Clear()
    {
        _start = 0;
        Count = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private double Aggregate(double seed, Func<double, double, double> combine)
    {
        if (Count == 0)
        {
            return 0;
        }

        var result = seed;
        for (var i = 0; i < Count; i++)
        {
            result = combine(result, this[i]);
        }

        return result;
    }
}
