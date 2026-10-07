namespace Talesmith.Rendering;

/// <summary>Consecutive draws that share a texture, material, layer and space and can be submitted in one call.</summary>
/// <remarks>
/// A batch either covers <see cref="InstanceCount"/> entries of <see cref="RenderFrame.Instances"/> starting at
/// <see cref="FirstInstance"/>, or, when <see cref="MeshInstances"/> is set, draws a <see cref="SpriteMesh"/> snapshot that backends
/// may cache by <see cref="MeshId"/> and <see cref="MeshVersion"/>.
/// </remarks>
public readonly record struct DrawBatch(
    Texture Texture,
    Material Material,
    int Layer,
    RenderSpace Space,
    int FirstInstance,
    int InstanceCount,
    SpriteInstance[]? MeshInstances,
    int MeshId,
    int MeshVersion)
{
    public bool IsMesh => MeshInstances is not null;
}
