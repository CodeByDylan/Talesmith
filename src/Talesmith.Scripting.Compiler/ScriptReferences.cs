namespace Talesmith.Scripting.Compiler;

/// <summary>Finds the assemblies scripts compile against.</summary>
public static class ScriptReferences
{
    private static readonly string[] EditorOnly =
        ["Talesmith.Editor", "Talesmith.UI", "Talesmith.App", "Talesmith.Build", "Talesmith.Scripting.Compiler", "Talesmith.Player", "Talesmith.Benchmarks"];

    private static readonly string[] SharedLibraries =
        ["Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions"];

    /// <summary>The framework and the engine's runtime assemblies.</summary>
    public static IReadOnlyList<string> Default() => [.. Framework(), .. Engine()];

    /// <summary>The assemblies of the .NET runtime the editor runs on.</summary>
    public static IReadOnlyList<string> Framework()
    {
        var trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
        return trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => string.Equals(Path.GetDirectoryName(path), runtime, StringComparison.Ordinal) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The engine's runtime assemblies in <paramref name="directory"/>, by default the editor's folder: everything a shipped game contains, without editor tools.</summary>
    public static IReadOnlyList<string> Engine(string? directory = null)
    {
        directory ??= AppContext.BaseDirectory;
        if (!Directory.Exists(directory))
            return [];
        return Directory.EnumerateFiles(directory, "*.dll")
            .Where(path => IsEngine(Path.GetFileNameWithoutExtension(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsEngine(string name)
    {
        if (SharedLibraries.Contains(name, StringComparer.OrdinalIgnoreCase))
            return true;
        if (!name.StartsWith("Talesmith.", StringComparison.Ordinal) || name.EndsWith(".Tests", StringComparison.Ordinal) || name.StartsWith("Talesmith.Samples", StringComparison.Ordinal))
            return false;
        return !EditorOnly.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
