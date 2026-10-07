using Microsoft.Extensions.Logging;

namespace Talesmith.VFX;

/// <summary>Source-generated log messages of the particle module.</summary>
internal static partial class ParticleLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The particle texture {Guid} is not in the asset catalog; the built-in texture is used")]
    public static partial void TextureMissing(ILogger logger, string guid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The particle preset {Guid} is not in the asset catalog; the emitter's own settings are used")]
    public static partial void PresetMissing(ILogger logger, string guid);

    [LoggerMessage(Level = LogLevel.Error, Message = "The particle asset {Guid} could not be loaded")]
    public static partial void AssetFailed(ILogger logger, Exception? error, string guid);
}
