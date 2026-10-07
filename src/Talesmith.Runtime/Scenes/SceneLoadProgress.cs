namespace Talesmith.Runtime.Scenes;

/// <summary>Counts the work a scene load has found and finished, such as the assets its entities use, for loading screens.</summary>
/// <remarks>
/// A scene reports into its <see cref="Scene.LoadProgress"/> while it loads: <see cref="Expect"/> as it finds work and <see cref="Advance"/>
/// as each piece is done. The total may grow during the load. Safe to use from any thread.
/// </remarks>
public sealed class SceneLoadProgress
{
    private int _total;
    private int _loaded;

    /// <summary>The work found so far.</summary>
    public int Total => Volatile.Read(ref _total);

    /// <summary>The work done so far.</summary>
    public int Loaded => Volatile.Read(ref _loaded);

    /// <summary>The share of the work done, from 0 to 1, or null while none was found.</summary>
    public double? Fraction
    {
        get
        {
            var total = Total;
            return total > 0 ? Math.Clamp(Loaded / (double)total, 0, 1) : null;
        }
    }

    /// <summary>Adds work the load has to do.</summary>
    public void Expect(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Interlocked.Add(ref _total, count);
    }

    /// <summary>Marks work as done.</summary>
    public void Advance(int count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Interlocked.Add(ref _loaded, count);
    }
}
