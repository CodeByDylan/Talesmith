namespace Talesmith.Screenshots.Capture;

/// <summary>Locates the repository the tool runs from.</summary>
internal static class Repository
{
    /// <summary>Gets the folder containing Talesmith.slnx, or the current directory when it cannot be found.</summary>
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Talesmith.slnx")))
                return directory.FullName;
        }

        return Directory.GetCurrentDirectory();
    }
}
