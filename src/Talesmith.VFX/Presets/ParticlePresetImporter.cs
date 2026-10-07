using System.Text.Json;
using Talesmith.Assets;

namespace Talesmith.VFX.Presets;

/// <summary>Imports <c>.tparticles</c> files as <see cref="ParticlePreset"/>s.</summary>
public sealed class ParticlePresetImporter(ParticleModuleRegistry modules) : AssetImporter<ParticlePreset>
{
    public override IReadOnlyList<string> Extensions { get; } = [ParticlePresetSerializer.Extension];

    public override async Task<ParticlePreset> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var stream = context.OpenRead();
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                return ParticlePresetSerializer.Deserialize(stream, modules);
            }
            catch (JsonException error)
            {
                throw new AssetException($"The particle preset {context.Path} is not valid: {error.Message}", error);
            }
        }
    }
}
