using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Talesmith.EndToEnd.Tests;

/// <summary>Loggers that keep every message, so tests can check what a game reported.</summary>
internal sealed class RecordingLoggers : ILoggerProvider
{
    private readonly ConcurrentQueue<(LogLevel Level, string Category, string Message)> _entries = new();

    public RecordingLoggers() => Factory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Information).AddProvider(this));

    public ILoggerFactory Factory { get; }

    public IReadOnlyList<string> Problems =>
        [.. _entries.Where(e => e.Level >= LogLevel.Warning).Select(e => $"{e.Level} {e.Category}: {e.Message}")];

    public bool Contains(string text) => _entries.Any(e => e.Message.Contains(text, StringComparison.Ordinal));

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(RecordingLoggers owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner._entries.Enqueue((logLevel, category, formatter(state, exception) + (exception is null ? "" : $" {exception}")));
    }
}
