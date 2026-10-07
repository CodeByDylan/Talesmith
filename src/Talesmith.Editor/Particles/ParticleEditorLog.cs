using Microsoft.Extensions.Logging;

namespace Talesmith.Editor.Particles;

internal static partial class ParticleEditorLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The particle preset {Path} could not be read")]
    public static partial void PresetUnreadable(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The particle preset {Path} could not be saved")]
    public static partial void PresetSaveFailed(ILogger logger, Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved the particle preset {Path}")]
    public static partial void PresetSaved(ILogger logger, string path);
}
