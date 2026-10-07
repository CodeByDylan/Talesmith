using System.Text.Json;
using System.Text.Json.Nodes;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.VFX.Presets;

/// <summary>Saves plugin particle modules in scenes and prefabs the same way presets do.</summary>
internal sealed class ParticleModuleValueConverter(ParticleModuleRegistry modules) : ValueConverter<IParticleModule>
{
    public override PropertyKind Kind => PropertyKind.Object;

    public override JsonNode Write(IParticleModule value, ICaptureContext context) => ParticlePresetSerializer.ModuleToJson(value, modules);

    public override IParticleModule Read(JsonNode node, IInstantiationContext context)
    {
        try
        {
            return ParticlePresetSerializer.ModuleFromJson(node, modules);
        }
        catch (JsonException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }
}
