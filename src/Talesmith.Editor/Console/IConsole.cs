namespace Talesmith.Editor.Console;

/// <summary>The editor console: messages from the editor, games, scripts, plugins and the asset pipeline.</summary>
/// <remarks>Safe to write from any thread. Logging through <c>ILogger</c> in the editor and in games started from it also ends up here.</remarks>
public interface IConsole
{
    /// <summary>A snapshot of the entries, oldest first.</summary>
    IReadOnlyList<ConsoleEntry> Entries { get; }

    /// <summary>Raised on the UI thread with the entries added since the last time, in order.</summary>
    event EventHandler<ConsoleEntriesEventArgs>? EntriesAdded;

    /// <summary>Raised on the UI thread after <see cref="Clear"/>.</summary>
    event EventHandler? Cleared;

    void Write(ConsoleEntry entry);

    void Clear();
}

public sealed class ConsoleEntriesEventArgs(IReadOnlyList<ConsoleEntry> entries) : EventArgs
{
    public IReadOnlyList<ConsoleEntry> Entries { get; } = entries;
}

public static class ConsoleExtensions
{
    public static void Info(this IConsole console, string message, ConsoleSource source = ConsoleSource.Editor, ConsoleTarget? target = null) =>
        console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Info, source, message) { Target = target });

    public static void Warning(this IConsole console, string message, ConsoleSource source = ConsoleSource.Editor, ConsoleTarget? target = null) =>
        console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Warning, source, message) { Target = target });

    public static void Error(this IConsole console, string message, Exception? exception = null, ConsoleSource source = ConsoleSource.Editor,
        ConsoleTarget? target = null) =>
        console.Write(new ConsoleEntry(DateTimeOffset.Now, ConsoleSeverity.Error, source, message) { Details = exception?.ToString(), Target = target });
}
