using Microsoft.Extensions.Logging;

namespace Talesmith.Scripting;

/// <summary>Writes to the engine log for scripts; see <see cref="Script.Log"/>.</summary>
/// <remarks>Messages are logged under the script's full type name and start with the entity's name, so the console can tell them apart.</remarks>
public readonly struct ScriptLog
{
    private readonly Script _script;

    internal ScriptLog(Script script) => _script = script;

    public void Debug(string message) => Write(LogLevel.Debug, message);

    public void Info(string message) => Write(LogLevel.Information, message);

    public void Warning(string message) => Write(LogLevel.Warning, message);

    public void Error(string message) => Write(LogLevel.Error, message);

    public void Error(Exception error, string message)
    {
        var logger = _script.Running.LoggerFor(_script);
        if (logger.IsEnabled(LogLevel.Error))
            logger.ScriptMessage(LogLevel.Error, error, ScriptRuntime.Describe(_script.World, _script.Entity), message);
    }

    private void Write(LogLevel level, string message)
    {
        var logger = _script.Running.LoggerFor(_script);
        if (logger.IsEnabled(level))
            logger.ScriptMessage(level, ScriptRuntime.Describe(_script.World, _script.Entity), message);
    }
}
