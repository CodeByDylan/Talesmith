using System.Text;

namespace Talesmith.Build.Packaging;

/// <summary>Turns titles into names that are valid file names on every platform.</summary>
public static class BuildNames
{
    private static readonly char[] Invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>A file name for a game's executable, such as "Hex Quest" for "Hex Quest: Tides?", or "Game" when nothing is left.</summary>
    public static string Executable(string? title)
    {
        var builder = new StringBuilder();
        foreach (var c in title ?? "")
        {
            if (char.IsControl(c) || Invalid.Contains(c))
                builder.Append(' ');
            else
                builder.Append(c);
        }

        var name = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (name.Length > 64)
            name = name[..64].TrimEnd('.', ' ');
        return name.Length == 0 || Reserved.Contains(name) ? "Game" : name;
    }
}
