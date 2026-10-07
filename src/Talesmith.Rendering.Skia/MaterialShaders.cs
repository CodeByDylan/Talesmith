namespace Talesmith.Rendering.Skia;

/// <summary>Keeps the shader of each material and texture pair; materials are immutable, so it changes only with the texture image.</summary>
internal sealed class MaterialShaders(ShaderLibrary library) : IDisposable
{
    private readonly Dictionary<(Material Material, int Texture), Entry> _entries = [];

    /// <summary>Gets a native shader for the material over the texture, or 0 when the material's shader is unavailable.</summary>
    public nint Get(Material material, ShaderSource source, int textureId, TextureSlot texture, long frame)
    {
        var key = (material, textureId);
        if (!_entries.TryGetValue(key, out var entry))
        {
            var shader = library.Get(source);
            if (shader is null)
                return 0;
            entry = new Entry(shader);
            _entries.Add(key, entry);
        }

        if (!ReferenceEquals(entry.Texture, texture))
        {
            SkiaNative.sk_shader_unref(entry.Shader);
            entry.Shader = entry.Compiled.CreateShader(texture.Shader.Handle, material.Parameters);
            entry.Texture = texture;
        }

        entry.LastUsedFrame = frame;
        return entry.Shader;
    }

    public void EvictUnusedSince(long frame)
    {
        foreach (var (key, entry) in _entries)
        {
            if (entry.LastUsedFrame >= frame)
                continue;
            SkiaNative.sk_shader_unref(entry.Shader);
            _entries.Remove(key);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            SkiaNative.sk_shader_unref(entry.Shader);
        _entries.Clear();
    }

    private sealed class Entry(RuntimeShader compiled)
    {
        public readonly RuntimeShader Compiled = compiled;
        public TextureSlot? Texture;
        public nint Shader;
        public long LastUsedFrame;
    }
}
