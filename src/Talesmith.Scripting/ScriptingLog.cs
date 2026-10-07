using Microsoft.Extensions.Logging;

namespace Talesmith.Scripting;

/// <summary>Source-generated log messages of the scripting module.</summary>
internal static partial class ScriptingLog
{
    [LoggerMessage(Message = "{Entity}: {Message}")]
    public static partial void ScriptMessage(this ILogger logger, LogLevel level, string entity, string message);

    [LoggerMessage(Message = "{Entity}: {Message}")]
    public static partial void ScriptMessage(this ILogger logger, LogLevel level, Exception error, string entity, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Script}.{Callback} on {Entity} failed")]
    public static partial void ScriptFailed(this ILogger logger, Exception error, string script, string callback, string entity);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Script} on {Entity} failed {Failures} times in a row and was disabled")]
    public static partial void ScriptDisabled(this ILogger logger, string script, string entity, int failures);

    [LoggerMessage(Level = LogLevel.Error, Message = "An asynchronous routine of {Script} on {Entity} failed")]
    public static partial void RoutineFailed(this ILogger logger, Exception error, string script, string entity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The script type {Type} on {Entity} is unknown, so its data is kept unchanged until the type exists again")]
    public static partial void ScriptTypeMissing(this ILogger logger, string type, string entity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Field} value of the script {Script} could not be read and keeps its default: {Reason}")]
    public static partial void ScriptFieldInvalid(this ILogger logger, string script, string field, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The script {Script} could not be created: {Reason}")]
    public static partial void ScriptCreateFailed(this ILogger logger, string script, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The script component of {Entity} already belongs to another entity; give each entity its own component")]
    public static partial void ScriptComponentShared(this ILogger logger, string entity);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded {Scripts} script types, {Systems} systems and {Components} components from {Assembly}")]
    public static partial void ScriptAssemblyLoaded(this ILogger logger, int scripts, int systems, int components, string assembly);

    [LoggerMessage(Level = LogLevel.Error, Message = "The compiled scripts at {Path} could not be loaded, so the game runs without them")]
    public static partial void ScriptAssemblyFailed(this ILogger logger, Exception error, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Hot reloaded {Count} scripts")]
    public static partial void HotReloaded(this ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scripts changed in a way that needs Play mode to restart: {Reasons}")]
    public static partial void HotReloadRefused(this ILogger logger, string reasons);
}
