using Microsoft.Extensions.Logging;

namespace Talesmith.Editor.Console;

/// <summary>Routes <see cref="ILogger"/> output to the editor console, choosing each entry's source from its category.</summary>
/// <param name="defaultSource">The source of categories that name no engine area: <see cref="ConsoleSource.Editor"/> for the editor,
/// <see cref="ConsoleSource.Game"/> for games started from it.</param>
public sealed class ConsoleLoggerProvider(IConsole console, ConsoleSource defaultSource, LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ConsoleLogger(console, categoryName, SourceOf(categoryName), minimumLevel);

    public void Dispose()
    {
    }

    /// <summary>The console source for a logger category.</summary>
    public ConsoleSource SourceOf(string category) => category switch
    {
        _ when category.StartsWith("Talesmith.Assets", StringComparison.Ordinal) => ConsoleSource.Assets,
        _ when category.StartsWith("Talesmith.Plugins", StringComparison.Ordinal) => ConsoleSource.Plugin,
        _ when category.StartsWith("Talesmith.Scripting", StringComparison.Ordinal) => ConsoleSource.Script,
        _ when category.StartsWith("Talesmith.Editor", StringComparison.Ordinal) => ConsoleSource.Editor,
        _ => defaultSource
    };

    private sealed class ConsoleLogger(IConsole console, string category, ConsoleSource source, LogLevel minimumLevel) : ILogger
    {
        private readonly string _shortCategory = category[(category.LastIndexOf('.') + 1)..];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;
            var severity = logLevel switch
            {
                LogLevel.Trace or LogLevel.Debug => ConsoleSeverity.Debug,
                LogLevel.Information => ConsoleSeverity.Info,
                LogLevel.Warning => ConsoleSeverity.Warning,
                _ => ConsoleSeverity.Error
            };
            var message = formatter(state, exception);
            if (exception is not null && string.IsNullOrEmpty(message))
                message = exception.Message;
            console.Write(new ConsoleEntry(DateTimeOffset.Now, severity, source, message, _shortCategory) { Details = exception?.ToString() });
        }
    }
}
