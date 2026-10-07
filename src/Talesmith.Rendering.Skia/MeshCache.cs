namespace Talesmith.Rendering.Skia;

/// <summary>Keeps the native vertices of each <see cref="SpriteMesh"/> and rebuilds them only when the mesh version changes.</summary>
internal sealed class MeshCache(QuadVertices builder) : IDisposable
{
    private readonly Dictionary<int, Entry> _entries = [];

    /// <summary>Gets native vertices for a mesh batch; they stay owned by the cache.</summary>
    public nint Get(in DrawBatch batch, long frame)
    {
        if (!_entries.TryGetValue(batch.MeshId, out var entry))
        {
            entry = new Entry();
            _entries.Add(batch.MeshId, entry);
        }

        if (entry.Version != batch.MeshVersion)
        {
            SkiaNative.sk_vertices_unref(entry.Vertices);
            entry.Vertices = builder.Create(batch.MeshInstances);
            entry.Version = batch.MeshVersion;
        }

        entry.LastUsedFrame = frame;
        return entry.Vertices;
    }

    public void EvictUnusedSince(long frame)
    {
        foreach (var (id, entry) in _entries)
        {
            if (entry.LastUsedFrame >= frame)
                continue;
            SkiaNative.sk_vertices_unref(entry.Vertices);
            _entries.Remove(id);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            SkiaNative.sk_vertices_unref(entry.Vertices);
        _entries.Clear();
    }

    private sealed class Entry
    {
        public nint Vertices;
        public int Version;
        public long LastUsedFrame;
    }
}
