using System.Text.Json;
using Talesmith.Assets;

namespace Talesmith.Samples.Cutscenes;

/// <summary>Imports ".cutscene" files, which are JSON scripts.</summary>
public sealed class CutsceneImporter : AssetImporter<CutsceneScript>
{
    public override IReadOnlyList<string> Extensions { get; } = [CutsceneScript.Extension];

    public override Task<CutsceneScript> ImportAsync(AssetImportContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = context.OpenRead();
            return Task.FromResult(CutsceneScript.Parse(stream));
        }
        catch (JsonException ex)
        {
            throw new AssetException($"The cutscene '{context.Path}' is not valid: {ex.Message}", ex);
        }
    }
}
