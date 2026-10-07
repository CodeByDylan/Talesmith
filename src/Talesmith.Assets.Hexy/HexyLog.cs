using Microsoft.Extensions.Logging;

namespace Talesmith.Assets.Hexy;

internal static partial class HexyLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path}: {Warning}")]
    public static partial void Warning(ILogger logger, string path, string warning);
}
