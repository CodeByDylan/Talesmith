using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Talesmith.Editor.Settings;

namespace Talesmith.Editor.Scripting;

/// <summary>Opens source files and the scripts' IDE project in the user's code editor.</summary>
public interface ICodeEditor
{
    /// <summary>Opens a file, at a line and column when they are above 0; returns false when nothing could be started.</summary>
    bool OpenFile(string path, int line = 0, int column = 0);

    /// <summary>Opens a C# project, or the folder containing it for editors that open folders.</summary>
    bool OpenProject(string projectFile);
}

/// <summary>Uses the command from <see cref="EditorSettings.ExternalEditor"/>; without one, Visual Studio Code or Rider when either is on the
/// PATH, and otherwise the system's default program.</summary>
public sealed class ExternalCodeEditor(ISettingsService settings) : ICodeEditor
{
    /// <summary>The commands used when the settings name none, in order of preference.</summary>
    public static IReadOnlyList<string> KnownCommands { get; } = ["code -g {file}:{line}:{column}", "rider --line {line} {file}"];

    public bool OpenFile(string path, int line = 0, int column = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var command = ResolveCommand();
        return command is null ? OpenWithSystem(path) : Run(command, path, line, column);
    }

    public bool OpenProject(string projectFile)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectFile);
        if (ResolveCommand() is not { } command)
            return OpenWithSystem(projectFile);
        var executable = Tokenize(command) is { Count: > 0 } words ? words[0] : "";
        var name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        var target = name.Contains("code", StringComparison.Ordinal) || name.Contains("codium", StringComparison.Ordinal)
            ? Path.GetDirectoryName(projectFile)!
            : projectFile;
        return Start(executable, [target]);
    }

    /// <summary>The command template in use, or null for the system's default program.</summary>
    public string? ResolveCommand()
    {
        if (!string.IsNullOrWhiteSpace(settings.Current.ExternalEditor))
            return settings.Current.ExternalEditor.Trim();
        return KnownCommands.FirstOrDefault(command => FindOnPath(Tokenize(command)[0]) is not null);
    }

    /// <summary>Splits a command line into words, keeping quoted parts together.</summary>
    public static IReadOnlyList<string> Tokenize(string command)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var quote = '\0';
        var started = false;
        foreach (var c in command)
        {
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
                else
                    word.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                started = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (started || word.Length > 0)
                    words.Add(word.ToString());
                word.Clear();
                started = false;
            }
            else
            {
                word.Append(c);
            }
        }

        if (started || word.Length > 0)
            words.Add(word.ToString());
        return words;
    }

    private static bool Run(string command, string path, int line, int column)
    {
        var words = Tokenize(command);
        if (words.Count == 0)
            return OpenWithSystem(path);
        var arguments = words.Skip(1).Select(word => word
            .Replace("{file}", path, StringComparison.Ordinal)
            .Replace("{line}", Math.Max(1, line).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{column}", Math.Max(1, column).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToList();
        if (!words.Any(w => w.Contains("{file}", StringComparison.Ordinal)))
            arguments.Add(path);
        return Start(words[0], arguments);
    }

    private static bool Start(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(FindOnPath(executable) ?? executable) { UseShellExecute = false };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static bool OpenWithSystem(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    private static string? FindOnPath(string executable)
    {
        if (Path.IsPathRooted(executable))
            return File.Exists(executable) ? executable : null;
        string[] extensions = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat", ""] : [""];
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder, executable + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }
}
