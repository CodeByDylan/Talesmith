using Microsoft.Extensions.Logging;

namespace Talesmith.Plugins;

internal static partial class PluginLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Loaded plugin {Id} {Version} from {Directory}")]
    public static partial void Loaded(ILogger logger, string id, Version version, string directory);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Disabled plugin {Id}: {Reason}")]
    public static partial void Disabled(ILogger logger, string id, string reason);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Plugin {Plugin} failed to load: {Reason}")]
    public static partial void Failed(ILogger logger, Exception? exception, string plugin, string reason);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Plugins: {Loaded} loaded, {Failed} failed, {Skipped} skipped, {Disabled} disabled")]
    public static partial void Summary(ILogger logger, int loaded, int failed, int skipped, int disabled);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning, Message = "Skipped plugin {Id}: {Reason}")]
    public static partial void Skipped(ILogger logger, string id, string reason);

    [LoggerMessage(EventId = 6, Level = LogLevel.Warning, Message = "Plugin {Id}: {Warning}")]
    public static partial void Warning(ILogger logger, string id, string warning);

    [LoggerMessage(EventId = 7, Level = LogLevel.Warning, Message = "Plugin {Id} used undeclared permission {Permission} to {Operation}; {Outcome}")]
    public static partial void PermissionViolation(ILogger logger, string id, string operation, string permission, string outcome);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Unloading {Count} plugins")]
    public static partial void Unloading(ILogger logger, int count);

    [LoggerMessage(EventId = 9, Level = LogLevel.Warning, Message = "Plugin {Id} is still loaded after unloading; something still references its code")]
    public static partial void StillLoaded(ILogger logger, string id);
}
