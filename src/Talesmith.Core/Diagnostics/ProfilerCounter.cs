namespace Talesmith.Diagnostics;

/// <summary>How a counter's value behaves between frames.</summary>
public enum CounterKind
{
    /// <summary>Starts at zero every frame and accumulates, such as draw calls or sprites drawn.</summary>
    PerFrame,

    /// <summary>Keeps its last value until set again, such as entity or loaded chunk counts.</summary>
    Gauge
}

/// <summary>A named number recorded with each frame, such as draw calls or entity count.</summary>
public sealed class ProfilerCounter
{
    private static readonly Lock RegistryLock = new();
    private static readonly Dictionary<string, ProfilerCounter> ByName = new(StringComparer.Ordinal);
    private static ProfilerCounter[] _all = [];

    private ProfilerCounter(int id, string name, CounterKind kind, string unit)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Unit = unit;
    }

    public int Id { get; }

    public string Name { get; }

    public CounterKind Kind { get; }

    /// <summary>A short unit label such as "calls", "sprites" or "bytes".</summary>
    public string Unit { get; }

    public static IReadOnlyList<ProfilerCounter> All => Volatile.Read(ref _all);

    public static int Count => Volatile.Read(ref _all).Length;

    /// <summary>Gets the counter with this name, registering it on first use.</summary>
    public static ProfilerCounter Get(string name, CounterKind kind = CounterKind.PerFrame, string unit = "")
    {
        lock (RegistryLock)
        {
            if (ByName.TryGetValue(name, out var existing))
                return existing;

            var counter = new ProfilerCounter(_all.Length, name, kind, unit);
            ByName[name] = counter;
            Volatile.Write(ref _all, [.. _all, counter]);
            return counter;
        }
    }

    public override string ToString() => Name;
}
