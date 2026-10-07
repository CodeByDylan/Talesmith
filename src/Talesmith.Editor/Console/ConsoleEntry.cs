using Talesmith.Assets;

namespace Talesmith.Editor.Console;

public enum ConsoleSeverity
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>Where a console entry comes from, which the console filters by.</summary>
public enum ConsoleSource
{
    Editor,
    Game,
    Script,
    Plugin,
    Assets
}

/// <summary>A message in the console.</summary>
/// <param name="Category">The logger category or another short origin, such as a plugin id.</param>
public sealed record ConsoleEntry(DateTimeOffset Time, ConsoleSeverity Severity, ConsoleSource Source, string Message, string? Category = null)
{
    /// <summary>More text shown when the entry is expanded, such as an exception's stack trace.</summary>
    public string? Details { get; init; }

    /// <summary>What double-clicking the entry opens, if anything.</summary>
    public ConsoleTarget? Target { get; init; }
}

/// <summary>Something a console entry refers to, which the editor can show.</summary>
public abstract record ConsoleTarget;

/// <summary>An entity of the open scene, by document id.</summary>
public sealed record EntityTarget(Guid Entity) : ConsoleTarget;

/// <summary>An asset, by guid or by path relative to the asset folder.</summary>
public sealed record AssetTarget(AssetGuid Guid, string? Path = null) : ConsoleTarget;

/// <summary>A line in a file, such as a script with a compile error.</summary>
/// <param name="Path">An absolute path or a path relative to the asset folder.</param>
public sealed record FileTarget(string Path, int Line = 0, int Column = 0) : ConsoleTarget;
