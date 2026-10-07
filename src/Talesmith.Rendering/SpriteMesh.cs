using Talesmith.Mathematics;

namespace Talesmith.Rendering;

/// <summary>A reusable set of sprite instances that rarely changes, such as one chunk of a tile map.</summary>
/// <remarks>
/// Backends may keep a mesh on the GPU and upload it again only when <see cref="Version"/> changes. Each <see cref="Update"/> stores a new array, so frames already handed to a render thread keep their snapshot.
/// </remarks>
public sealed class SpriteMesh
{
    private static int _nextId;

    public SpriteMesh()
    {
        Id = Interlocked.Increment(ref _nextId);
    }

    public int Id { get; }

    /// <summary>Increases with every update.</summary>
    public int Version { get; private set; }

    public SpriteInstance[] Instances { get; private set; } = [];

    /// <summary>The world bounds of every instance, for culling the whole mesh.</summary>
    public Rect2 Bounds { get; private set; }

    public void Update(ReadOnlySpan<SpriteInstance> instances)
    {
        Instances = instances.ToArray();
        var bounds = Rect2.Empty;
        foreach (ref readonly var instance in instances)
            bounds = bounds.Union(instance.Bounds);
        Bounds = bounds;
        Version++;
    }
}
