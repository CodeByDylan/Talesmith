using System.Numerics;
using Talesmith.Assets.Json;
using Talesmith.Rendering;

namespace Talesmith.Assets.Materials;

/// <summary>The contents of a <c>.tmaterial</c> file.</summary>
/// <remarks>
/// <code>
/// {
///   "version": 1,
///   "blend": "additive",
///   "shader": "5e0c4a1b2d3f4e5a6b7c8d9e0f1a2b3c",
///   "parameters": [ [1, 1, 1, 0.8] ]
/// }
/// </code>
/// The shader is the guid of a .tshader asset, or a path relative to the material; without one the material only sets the blend mode.
/// </remarks>
public sealed record MaterialDefinition
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public BlendMode Blend { get; init; } = BlendMode.Alpha;

    public string? Shader { get; init; }

    /// <summary>Up to four values passed to the shader as <c>params</c>.</summary>
    public IReadOnlyList<Vector4> Parameters { get; init; } = [];
}

/// <summary>Imports <c>.tmaterial</c> files as <see cref="Material"/>s, loading their shaders.</summary>
/// <remarks>Each material file yields one shared instance, so sprites using it batch together.</remarks>
public sealed class MaterialImporter : AssetImporter<Material>
{
    public const string ImporterId = "material";

    public override IReadOnlyList<string> Extensions { get; } = [".tmaterial"];

    public override string Id => ImporterId;

    public override async Task<Material> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        MaterialDefinition definition;
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
            definition = await AssetJson.DeserializeAsync<MaterialDefinition>(stream, "The material", cancellationToken).ConfigureAwait(false);
        if (definition.Version > MaterialDefinition.CurrentVersion)
            throw new AssetException($"The material has version {definition.Version}, but this version of Talesmith reads up to {MaterialDefinition.CurrentVersion}.");
        if (definition.Parameters.Count > 4)
            throw new AssetException($"The material has {definition.Parameters.Count} parameters; a material has at most four.");

        ShaderSource? shader = null;
        if (!string.IsNullOrWhiteSpace(definition.Shader))
        {
            var path = AssetGuid.TryParse(definition.Shader, null, out var guid) ? context.ResolveGuid(guid) : context.Resolve(definition.Shader);
            shader = await context.Assets.LoadAsync<ShaderSource>(path, cancellationToken).ConfigureAwait(false);
        }

        return new Material(definition.Blend, shader, definition.Parameters.ToArray());
    }
}
