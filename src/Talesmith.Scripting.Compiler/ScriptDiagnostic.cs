namespace Talesmith.Scripting.Compiler;

public enum ScriptDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>A compiler or analyzer message about a script, located for a console that navigates to it.</summary>
/// <param name="Id">The diagnostic id, such as "CS0103" or "TS1001".</param>
/// <param name="FilePath">The full path of the file, or null for messages about the whole compilation.</param>
/// <param name="RelativePath">The path relative to the project folder with forward slashes, for display.</param>
/// <param name="Line">The 1-based line where the problem starts, or 0 without a file.</param>
/// <param name="Column">The 1-based column where the problem starts, or 0 without a file.</param>
/// <param name="EndLine">The 1-based line where the problem ends.</param>
/// <param name="EndColumn">The 1-based column where the problem ends.</param>
/// <param name="HelpLink">A page explaining the message, when there is one.</param>
public sealed record ScriptDiagnostic(
    string Id,
    ScriptDiagnosticSeverity Severity,
    string Message,
    string? FilePath,
    string? RelativePath,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string? HelpLink = null)
{
    public bool IsError => Severity == ScriptDiagnosticSeverity.Error;

    /// <summary>Formats the message like the C# compiler: <c>assets/scripts/Player.cs(12,9): warning TS1002: …</c>.</summary>
    public override string ToString()
    {
        var severity = Severity switch
        {
            ScriptDiagnosticSeverity.Error => "error",
            ScriptDiagnosticSeverity.Warning => "warning",
            _ => "info"
        };
        return RelativePath is null ? $"{severity} {Id}: {Message}" : $"{RelativePath}({Line},{Column}): {severity} {Id}: {Message}";
    }
}
