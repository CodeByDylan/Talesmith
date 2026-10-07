namespace Talesmith.Diagnostics;

/// <summary>A named section of code whose time is measured each frame.</summary>
/// <remarks>Register markers once, typically in a static field, and reuse them: <c>static readonly ProfilerMarker Marker = ProfilerMarker.Get("Physics");</c></remarks>
public sealed class ProfilerMarker
{
    private static readonly Lock RegistryLock = new();
    private static readonly Dictionary<string, ProfilerMarker> ByName = new(StringComparer.Ordinal);
    private static ProfilerMarker[] _all = [];

    private ProfilerMarker(int id, string name, string category)
    {
        Id = id;
        Name = name;
        Category = category;
    }

    /// <summary>A dense id used to index per-frame sample arrays.</summary>
    public int Id { get; }

    public string Name { get; }

    /// <summary>A grouping such as "Engine", "Systems" or "Rendering", used by reports and the overlay.</summary>
    public string Category { get; }

    public static IReadOnlyList<ProfilerMarker> All => Volatile.Read(ref _all);

    public static int Count => Volatile.Read(ref _all).Length;

    /// <summary>Gets the marker with this name, registering it on first use.</summary>
    public static ProfilerMarker Get(string name, string category = "General")
    {
        lock (RegistryLock)
        {
            if (ByName.TryGetValue(name, out var existing))
                return existing;

            var marker = new ProfilerMarker(_all.Length, name, category);
            ByName[name] = marker;
            Volatile.Write(ref _all, [.. _all, marker]);
            return marker;
        }
    }

    public override string ToString() => Name;
}
