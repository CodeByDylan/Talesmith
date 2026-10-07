using Microsoft.Extensions.Logging;

namespace Talesmith.Runtime;

/// <summary>Source-generated log messages of the runtime.</summary>
internal static partial class RuntimeLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Frame {Frame} failed")]
    public static partial void FrameFailed(this ILogger logger, Exception error, long frame);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The input profile {Path} could not be read; using the default bindings")]
    public static partial void InputProfileUnreadable(this ILogger logger, Exception error, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "The start scene {Scene} could not be loaded")]
    public static partial void StartSceneFailed(this ILogger logger, Exception error, string scene);

    [LoggerMessage(Level = LogLevel.Error, Message = "A game task failed")]
    public static partial void GameTaskFailed(this ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chunk {Chunk} could not be decoded")]
    public static partial void ChunkDecodeFailed(this ILogger logger, Exception error, string chunk);

    [LoggerMessage(Level = LogLevel.Error, Message = "The scene {Scene} failed to load")]
    public static partial void SceneLoadFailed(this ILogger logger, Exception error, string scene);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scene {Scene} is active")]
    public static partial void SceneActive(this ILogger logger, string scene);

    [LoggerMessage(Level = LogLevel.Error, Message = "The system {System} failed")]
    public static partial void SystemFailed(this ILogger logger, Exception error, string system);

    [LoggerMessage(Level = LogLevel.Error, Message = "The system {System} failed {Count} times in a row and was disabled")]
    public static partial void SystemDisabled(this ILogger logger, Exception error, string system, int count);
}
